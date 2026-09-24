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

    private readonly SemaphoreSlim _changing = new(1, 1);

    /// <summary>Puts a new set of settings into force and saves them.</summary>
    /// <param name="settings">The new settings. They become the app's, so the caller must not keep editing them.</param>
    /// <returns>What happened, including anything the settings were only warned about.</returns>
    public async Task<SettingsApplyResult> ApplyAsync(SmartZoomSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _changing.WaitAsync().ConfigureAwait(false);
        try
        {
            return await ApplyUnderGateAsync(settings).ConfigureAwait(false);
        }
        finally
        {
            _changing.Release();
        }
    }

    /// <summary>
    /// Applies the settings file as it is on disk right now, whoever edited it. A file that cannot be read as
    /// it stands is rejected, never replaced: the user's edits are the point of this call.
    /// </summary>
    public Task<SettingsApplyResult> ReloadAsync() =>
        store.TryLoad(out var settings, out var error)
            ? ApplyAsync(settings)
            : Task.FromResult(SettingsApplyResult.Rejected([SettingsProblem.Error(FileSection, error)]));

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
            _changing.Release();
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
            _changing.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _changing.Dispose();

    /// <summary>Everything wrong with a candidate set of settings, without changing anything.</summary>
    private IReadOnlyList<SettingsProblem> Validate(SmartZoomSettings settings) =>
        SettingsValidator.Validate(settings, engine.Current.Router.Adapters, systemInput.DoubleClickTimeMs);

    private async Task<SettingsApplyResult> ApplyUnderGateAsync(SmartZoomSettings settings)
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

        return Save(settings, problems);
    }

    /// <summary>Writes settings that are already in force; a write that fails costs nothing but persistence.</summary>
    private SettingsApplyResult Save(SmartZoomSettings settings, IReadOnlyList<SettingsProblem> problems)
    {
        try
        {
            store.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The app is running the new settings correctly; they just will not survive a restart.
            LogSaveFailed(ex);
            return SettingsApplyResult.AppliedButNotSaved(problems, ex.Message);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "The settings are in force but could not be written to disk; they will be lost on restart.")]
    private partial void LogSaveFailed(Exception exception);
}
