using Microsoft.Extensions.Logging;

using Serilog.Core;
using Serilog.Events;

using SmartZoom.App.Diagnostics;
using SmartZoom.App.Hosting;
using SmartZoom.Core.Input;
using SmartZoom.Core.Settings;
using SmartZoom.Interop;

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
/// Applying can block for as long as a zoom in flight takes, so this must not run on the UI thread.
/// </para>
/// </remarks>
internal sealed partial class SettingsApplier(
    SettingsHolder holder,
    SettingsStore store,
    ZoomPipelineFactory factory,
    ZoomEngine engine,
    ITriggerSource triggers,
    DiagnosticRecorder recorder,
    LoggingLevelSwitch logLevel,
    ILogger<SettingsApplier> logger)
{
    /// <summary>Everything wrong with a candidate set of settings, without changing anything.</summary>
    /// <param name="settings">The candidate.</param>
    public IReadOnlyList<SettingsProblem> Validate(SmartZoomSettings settings) =>
        SettingsValidator.Validate(settings, engine.Current.Router.Adapters, SystemInput.DoubleClickTimeMs);

    /// <summary>Puts a new set of settings into force and saves them.</summary>
    /// <param name="settings">The new settings. They become the app's, so the caller must not keep editing them.</param>
    /// <returns>What happened, including anything the settings were only warned about.</returns>
    public async Task<SettingsApplyResult> ApplyAsync(SmartZoomSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var problems = Validate(settings);
        if (problems.Any(p => p.Severity == SettingsProblemSeverity.Error))
            return SettingsApplyResult.Rejected(problems);

        // Everything that can throw, before anything has changed.
        List<TriggerDefinition> definitions;
        ZoomPipeline pipeline;
        try
        {
            definitions = [.. settings.Triggers.Select(t => t.ToDefinition(SystemInput.DoubleClickTimeMs))];
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
        await engine.ReplaceAsync(pipeline).ConfigureAwait(false);
        logLevel.MinimumLevel = Serilog(settings.Logging.Level);

        // The diagnostics switch is carried live, like triggers.Enabled: nothing has to be rebuilt for it,
        // and it is the only control the user has over a privacy feature - it may not wait for a restart.
        recorder.Enabled = settings.Diagnostics.Enabled;
        holder.Replace(settings);

        LogApplied();

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

    /// <summary>Applies the settings file as it is on disk right now, whoever edited it.</summary>
    public Task<SettingsApplyResult> ReloadAsync() => ApplyAsync(store.Load());

    /// <summary>
    /// Turns zooming on or off. Kept apart from <see cref="ApplyAsync"/> because nothing has to be rebuilt:
    /// the trigger source carries this one live.
    /// </summary>
    /// <param name="enabled">Whether triggers should be acted on.</param>
    public void SetEnabled(bool enabled)
    {
        var settings = holder.Current;
        settings.Enabled = enabled;
        triggers.Enabled = enabled;

        try
        {
            store.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(ex);
        }
    }

    /// <summary>Maps the setting's level onto Serilog's, which is the same ladder under another name.</summary>
    private static LogEventLevel Serilog(LogLevel level) => level switch
    {
        LogLevel.Trace => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        LogLevel.Critical => LogEventLevel.Fatal,
        _ => LogEventLevel.Fatal,
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "New settings are in force.")]
    private partial void LogApplied();

    [LoggerMessage(Level = LogLevel.Error, Message = "The settings could not be built, so the previous ones are still running.")]
    private partial void LogBuildFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The settings are in force but could not be written to disk; they will be lost on restart.")]
    private partial void LogSaveFailed(Exception exception);
}
