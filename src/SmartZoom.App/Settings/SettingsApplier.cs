using Microsoft.Extensions.Logging;

using Serilog.Core;
using Serilog.Events;

using SmartZoom.App.Diagnostics;
using SmartZoom.App.Hosting;
using SmartZoom.Core.Input;
using SmartZoom.Core.Settings;

namespace SmartZoom.App.Settings;

/// <summary>
/// The one place settings change. Validates them, puts them into force, and only then writes the file.
/// </summary>
/// <remarks>
/// <para>
/// The order is deliberate. A file written before the change was applied would describe a state the process
/// was never in, and if applying then failed the user would restart into settings that may not work. Written
/// afterwards, the file always describes something that ran at least once.
/// </para>
/// <para>
/// Nothing is mutated until everything that can fail has already run: the trigger definitions are built, the
/// pipeline is constructed, and both throw on bad values while the old ones are still in force.
/// </para>
/// <para>
/// One change at a time: every entry point takes the same gate, so two writers can never interleave on the
/// settings file or publish through <see cref="SettingsHolder"/> out of order. Applying can block for as long
/// as a zoom in flight takes, so this must not run on the UI thread.
/// </para>
/// </remarks>
internal sealed partial class SettingsApplier(
    SettingsHolder holder,
    SettingsStore store,
    ZoomPipelineFactory factory,
    ZoomEngine engine,
    ITriggerSource triggers,
    ISystemInput systemInput,
    DiagnosticRecorder recorder,
    LoggingLevelSwitch logLevel,
    ILogger<SettingsApplier> logger) : IDisposable
{
    private const string FileSection = "Settings file";

    private const string UnreadableFileDetail =
        "the change is in force but was not saved. The settings file could not be read when SmartZoom started, so it is left as it is; SmartZoom will use it as soon as it can be read, and this change will then be replaced by what the file says.";

    private const string RefusedFileDetail =
        "the change is in force but was not saved. The settings file could not be read when SmartZoom started, and once it could, what it holds was refused (the log says why), so it is left as it is. SmartZoom will use it as soon as it is corrected, and this change will then be replaced by what the file says.";

    private const string UnreadableFileNotice =
        "Your settings file is in use by another program, so SmartZoom is running on its defaults and not saving changes over it. Your settings come back as soon as the file can be read.";

    private const string RefusedFileNotice =
        "Your settings file was refused (the log says why), so SmartZoom is running on its defaults and not saving changes over it. It will use the file as soon as it is corrected.";

    private readonly SemaphoreSlim _changing = new(1, 1);

    // While SettingsStore.FileUnreadable, and changed only under _changing: whether the file has since been read
    // and refused, and why (each new reason is logged once); whether the user has been told about the current
    // state, and the notice still to be raised once the gate is released; and whether the last attempt found no
    // file at all.
    private bool _fileRefused;
    private string? _refusedBecause;
    private bool _noticeGiven;
    private string? _pendingNotice;
    private bool _missingOnce;

    // A missing file is taken as deleted only when it has stayed missing from one attempt to the next, which an
    // attempt alone cannot tell: it sees one instant, and an editor that saves by deleting the file and renaming
    // its new copy into place, over and over, can be missing at every instant looked at. So from the first miss
    // the folder is watched, and any file appearing under the name (or the watcher losing track) since the
    // previous attempt means it is not gone. _appearances is counted on the watcher's own threads.
    private FileSystemWatcher? _watcher;
    private int _appearances;
    private int _appearancesAtMiss;
    private bool _recreateFailureLogged;

    /// <summary>
    /// Raised, at most once per state, when the settings in force are not being saved because the settings file
    /// is the user's and could not be put into force: when a change is first held back while the file cannot be
    /// read, and again when it is read and refused. Carries a sentence for the user. Raised after the change has
    /// finished, outside the gate, on whatever thread applied it; a listener that touches a window must marshal
    /// to its own thread.
    /// </summary>
    public event EventHandler<string>? SavingHeldBack;

    /// <summary>Makes a change to the settings in force and saves the result.</summary>
    /// <param name="change">
    /// What to change. It is handed a copy of the settings in force, made inside the gate, and may edit it
    /// freely; it must not keep a reference to it afterwards.
    /// </param>
    /// <returns>What happened, including anything the settings were only warned about.</returns>
    /// <remarks>
    /// The change rather than the result, because the caller cannot safely build the result. Between a caller
    /// reading <see cref="SettingsHolder.Current"/> and reaching this gate, anything else may have changed
    /// something — and the gate is held for as long as a zoom in flight takes, so that window is seconds wide,
    /// not microseconds. Applying a copy read before it would silently put back every field somebody else had
    /// just changed: switching off from the tray and then moving a slider used to turn zooming back on.
    /// </remarks>
    public async Task<SettingsApplyResult> ApplyAsync(Action<SmartZoomSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        await _changing.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = SettingsStore.Clone(holder.Current);
            change(settings);
            return await ApplyUnderGateAsync(settings).ConfigureAwait(false);
        }
        finally
        {
            ReleaseGate();
        }
    }

    /// <summary>
    /// Applies the settings file as it is on disk right now, whoever edited it. A file that cannot be read as
    /// it stands is rejected, never replaced: the user's edits are the point of this call.
    /// </summary>
    /// <remarks>
    /// The one caller that legitimately replaces the settings wholesale rather than changing them: the file
    /// IS the new state, so there is nothing of the old one to preserve.
    /// </remarks>
    public Task<SettingsApplyResult> ReloadAsync() => ReloadGatedAsync(onlyWhileUnreadable: false);

    /// <summary>
    /// One attempt to end a startup on defaults (<see cref="SettingsStore.FileUnreadable"/>) by putting the
    /// settings file into force.
    /// </summary>
    /// <returns>
    /// True once there is nothing left to wait for: the file is in force, or it is gone and has been written
    /// again. A file that cannot be opened, or that was read and refused, is tried again: a refused file may be
    /// half written, or about to be corrected.
    /// </returns>
    public async Task<bool> RetryUnreadableFileAsync()
    {
        await ReloadGatedAsync(onlyWhileUnreadable: true).ConfigureAwait(false);
        return !store.FileUnreadable;
    }

    /// <summary>The reload itself; see <see cref="ReloadAsync"/>.</summary>
    /// <param name="onlyWhileUnreadable">
    /// Do nothing unless the fallback is still on. Checked inside the gate: Reload settings file may have put the
    /// file into force since the retry last looked, and a reload after that is an ordinary one, which writes the
    /// file back and drops the user's comments.
    /// </param>
    private async Task<SettingsApplyResult> ReloadGatedAsync(bool onlyWhileUnreadable = false)
    {
        await _changing.WaitAsync().ConfigureAwait(false);
        try
        {
            if (onlyWhileUnreadable && !store.FileUnreadable)
                return SettingsApplyResult.Applied([]);

            // Read inside the gate: a change waiting here must not write over the file before what it says is
            // in force.
            var unreadable = store.FileUnreadable;
            if (!store.TryLoad(out var settings, out var error, out var failure, wait: false))
            {
                var rejected = SettingsApplyResult.Rejected([SettingsProblem.Error(FileSection, error)]);
                if (!unreadable)
                    return rejected;

                if (failure != SettingsReadFailure.Missing)
                {
                    StopWatching();
                    if (failure == SettingsReadFailure.Invalid)
                        Refuse(error);
                    return rejected;
                }

                // The first miss starts the watch; only a later one can tell whether the file stayed gone.
                var watching = Watching();
                if (!_missingOnce || !watching)
                {
                    _missingOnce = true;
                    return rejected;
                }

                var seen = Volatile.Read(ref _appearances);
                if (seen != _appearancesAtMiss)
                {
                    // Back and gone again since the last attempt: somebody is saving it. Count from here.
                    _appearancesAtMiss = seen;
                    return rejected;
                }

                // Deleted while it could not be read: nothing is left to protect, and holding writes back would
                // lose this session's changes as well. Write down what is in force, as a new file only: one put
                // in place this very moment is the user's, and is read next time.
                try
                {
                    if (!store.TryCreate(holder.Current))
                        return rejected;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Retried every attempt; said once, or a read-only folder fills the log.
                    if (!_recreateFailureLogged)
                        LogRecreateFailed(ex);
                    _recreateFailureLogged = true;
                    return rejected;
                }

                TrustFileAgain();
                LogUnreadableFileGone();
                return SettingsApplyResult.Applied([]);
            }

            StopWatching();
            if (!unreadable)
                return await ApplyUnderGateAsync(settings).ConfigureAwait(false);

            // The file is what is being put into force, so it is not written back: that would only reformat it
            // and drop the user's comments. A file it refuses stays protected until it is corrected.
            // Quietly: a refusal is logged by Refuse, once per reason, rather than on every retry.
            var result = await ApplyUnderGateAsync(settings, save: false, quiet: true).ConfigureAwait(false);
            if (!result.InForce)
            {
                Refuse(string.Join("; ", result.Problems.Where(p => p.Severity == SettingsProblemSeverity.Error)));
                return result;
            }

            TrustFileAgain();
            LogUnreadableFileInForce();
            return SettingsApplyResult.Applied(result.Problems);
        }
        finally
        {
            ReleaseGate();
        }
    }

    /// <summary>
    /// The file was read and its settings cannot be used. Each new reason is logged once (a retry finding the
    /// same one again says nothing more), and the user is told the first time.
    /// </summary>
    private void Refuse(string why)
    {
        if (why != _refusedBecause)
            LogUnreadableFileRefused(why);
        _refusedBecause = why;

        if (!_fileRefused)
        {
            _fileRefused = true;
            _noticeGiven = false;
        }

        GiveNotice();
    }

    private void TrustFileAgain()
    {
        store.TrustFileAgain();
        _fileRefused = false;
        _refusedBecause = null;
        _noticeGiven = false;
        _recreateFailureLogged = false;
        StopWatching();
    }

    /// <summary>
    /// Makes sure the folder is being watched for the file, from this miss on. False when it cannot be, in which
    /// case the file is never taken as deleted: waiting is the safe side.
    /// </summary>
    private bool Watching()
    {
        if (_watcher is not null)
            return true;

        try
        {
            var watcher = new FileSystemWatcher(Path.GetDirectoryName(store.FilePath)!, Path.GetFileName(store.FilePath))
            {
                NotifyFilter = NotifyFilters.FileName,
            };
            watcher.Created += (_, _) => Interlocked.Increment(ref _appearances);
            watcher.Renamed += (_, _) => Interlocked.Increment(ref _appearances);
            watcher.Error += (_, _) => Interlocked.Increment(ref _appearances);
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
            _appearancesAtMiss = Volatile.Read(ref _appearances);
            return true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            LogCannotWatch(ex);
            return false;
        }
    }

    private void StopWatching()
    {
        _missingOnce = false;
        _watcher?.Dispose();
        _watcher = null;
    }

    /// <summary>Queues the notice for the current state, if the user has not had it; see <see cref="ReleaseGate"/>.</summary>
    private void GiveNotice()
    {
        if (_noticeGiven)
            return;

        _noticeGiven = true;
        _pendingNotice = _fileRefused ? RefusedFileNotice : UnreadableFileNotice;
    }

    /// <summary>
    /// Leaves the gate, then raises any notice queued inside it: a listener may call back into the applier, which
    /// would deadlock on the gate, and a listener that throws must not fail a change already in force.
    /// </summary>
    private void ReleaseGate()
    {
        var notice = _pendingNotice;
        _pendingNotice = null;
        _changing.Release();

        if (notice is null)
            return;

        try
        {
            SavingHeldBack?.Invoke(this, notice);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogNoticeFailed(ex);
        }
    }

    /// <summary>
    /// Turns zooming on or off. Kept apart from <see cref="ApplyAsync"/> because nothing has to be rebuilt:
    /// the trigger source carries this one live.
    /// </summary>
    /// <param name="enabled">Whether triggers should be acted on.</param>
    /// <returns>Applied, or applied but not saved when the file could not be written.</returns>
    public async Task<SettingsApplyResult> SetEnabledAsync(bool enabled)
    {
        await _changing.WaitAsync().ConfigureAwait(false);
        try
        {
            // A copy, so the settings already published are never edited underneath whoever is reading them.
            var settings = SettingsStore.Clone(holder.Current);
            settings.Enabled = enabled;
            triggers.Enabled = enabled;
            holder.Replace(settings);

            return Save(settings, []);
        }
        finally
        {
            ReleaseGate();
        }
    }

    /// <summary>
    /// Records which palette the settings window wears. Kept apart from <see cref="ApplyAsync"/> for the same
    /// reason <see cref="SetEnabledAsync"/> is: nothing has to be rebuilt, and rebuilding the whole zoom
    /// pipeline to remember a colour would put a zoom in flight in the way of a click on a theme button.
    /// </summary>
    /// <param name="appearance">The appearance to remember.</param>
    /// <returns>Applied, or applied but not saved when the file could not be written.</returns>
    public async Task<SettingsApplyResult> SetAppearanceAsync(AppearanceMode appearance)
    {
        await _changing.WaitAsync().ConfigureAwait(false);
        try
        {
            // A copy, so the settings already published are never edited underneath whoever is reading them.
            var settings = SettingsStore.Clone(holder.Current);
            settings.Appearance = appearance;
            holder.Replace(settings);

            return Save(settings, []);
        }
        finally
        {
            ReleaseGate();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _watcher?.Dispose();
        _changing.Dispose();
    }

    /// <summary>Everything wrong with a candidate set of settings, without changing anything.</summary>
    private IReadOnlyList<SettingsProblem> Validate(SmartZoomSettings settings) =>
        SettingsValidator.Validate(settings, engine.Current.Router.Adapters, systemInput.DoubleClickTimeMs);

    private async Task<SettingsApplyResult> ApplyUnderGateAsync(SmartZoomSettings settings, bool save = true, bool quiet = false)
    {
        var problems = Validate(settings);
        if (problems.Any(p => p.Severity == SettingsProblemSeverity.Error))
            return SettingsApplyResult.Rejected(problems);

        // Everything that can throw, before anything has changed.
        List<TriggerDefinition> definitions;
        ZoomPipeline pipeline;
        try
        {
            definitions = [.. settings.Triggers.Select(t => t.ToDefinition(systemInput.DoubleClickTimeMs))];
            pipeline = factory.Build(settings);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            // Validation should have caught this; if it did not, the old settings are still running.
            if (!quiet)
                LogBuildFailed(ex);
            return SettingsApplyResult.Rejected([.. problems, SettingsProblem.Error("Settings", ex.Message)]);
        }

        // From here nothing throws, so the app cannot be left half-changed.
        triggers.SetTriggers(definitions);
        triggers.Enabled = settings.Enabled;
        var swappedCleanly = await engine.ReplaceAsync(pipeline).ConfigureAwait(false);
        logLevel.MinimumLevel = ToSerilogLevel(settings.Logging.Level);

        // The diagnostics switch is carried live, like triggers.Enabled: nothing has to be rebuilt for it,
        // and it is the only control the user has over a privacy feature - it may not wait for a restart.
        recorder.Enabled = settings.Diagnostics.Enabled;
        holder.Replace(settings);

        LogApplied();

        if (!swappedCleanly)
        {
            // The engine has already logged it; the person who pressed Save is told here.
            problems =
            [
                .. problems,
                SettingsProblem.Warning(
                    "Zoom",
                    "The settings were applied while a zoom was running; windows zoomed by a strategy that changed will zoom in again rather than come back."),
            ];
        }

        return save ? Save(settings, problems) : SettingsApplyResult.Applied(problems);
    }

    /// <summary>Writes settings that are already in force; a write that fails costs nothing but persistence.</summary>
    private SettingsApplyResult Save(SmartZoomSettings settings, IReadOnlyList<SettingsProblem> problems)
    {
        // Running on defaults because the file could not be read at startup: the file still holds the user's
        // settings, and a change made on top of the defaults would replace all of them. SettingsFileRetry puts
        // the file into force as soon as it can be read, and writes are let through from then on.
        if (store.FileUnreadable)
        {
            LogSaveDeferred(_fileRefused ? "the settings file was refused when it was read" : "the settings file has not been readable since startup");
            GiveNotice();
            return SettingsApplyResult.AppliedButNotSaved(problems, _fileRefused ? RefusedFileDetail : UnreadableFileDetail);
        }

        try
        {
            store.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The app is running the new settings correctly; they just will not survive a restart.
            LogSaveFailed(ex);
            return SettingsApplyResult.AppliedButNotSaved(problems, $"the change is in force but could not be written, so it will be lost on restart ({ex.Message})");
        }

        return SettingsApplyResult.Applied(problems);
    }

    /// <summary>Maps the setting's level onto Serilog's, which is the same ladder under another name.</summary>
    /// <summary>The Serilog level a settings level stands for; <see cref="LogLevel.None"/> turns the log off.</summary>
    internal static LogEventLevel ToSerilogLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        LogLevel.Critical => LogEventLevel.Fatal,
        LogLevel.None => LevelAlias.Off,
        _ => LogEventLevel.Fatal,
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "New settings are in force.")]
    private partial void LogApplied();

    [LoggerMessage(Level = LogLevel.Error, Message = "The settings could not be built, so the previous ones are still running.")]
    private partial void LogBuildFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "The settings file can be read again; its settings are now in force.")]
    private partial void LogUnreadableFileInForce();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The settings file can be read again, but it was refused, so the defaults stay in force and the file is left as it is until it is corrected: {Problems}")]
    private partial void LogUnreadableFileRefused(string problems);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The settings folder cannot be watched, so a settings file that has gone missing will not be taken as deleted; the defaults stay in force.")]
    private partial void LogCannotWatch(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The settings file was removed while it could not be read, and writing it again failed; SmartZoom tries again shortly.")]
    private partial void LogRecreateFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telling the user that the settings are not being saved failed.")]
    private partial void LogNoticeFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "The settings file was removed while it could not be read; it has been written again from the settings in force.")]
    private partial void LogUnreadableFileGone();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The settings are in force but were not written: {Why}, and writing would replace it.")]
    private partial void LogSaveDeferred(string why);

    [LoggerMessage(Level = LogLevel.Error, Message = "The settings are in force but could not be written to disk; they will be lost on restart.")]
    private partial void LogSaveFailed(Exception exception);
}
