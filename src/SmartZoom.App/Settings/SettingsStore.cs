using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using SmartZoom.Core.Settings;

namespace SmartZoom.App.Settings;

/// <summary>Loads and saves <see cref="SmartZoomSettings"/> as human-editable JSON.</summary>
internal sealed partial class SettingsStore(AppPaths paths, ILogger<SettingsStore> logger, int attempts = 10, TimeSpan? retryDelay = null)
{
    // How long a file another process is holding is waited for: an editor saving it, an antivirus scan, a backup
    // or a sync client. Those let go well inside a second; ten tries 200 ms apart cost at most two at startup.
    // Only a test passes these.
    private readonly int _attempts = Math.Max(1, attempts);
    private readonly TimeSpan _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(200);

    private volatile bool _fileUnreadable;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Whether the settings in force are defaults standing in for a file that exists but could not be read.
    /// While it is, the file is still the user's settings: nothing may be written over it, and it should be
    /// read again until what it says is in force. Cleared by a load that succeeds, or by
    /// <see cref="TrustFileAgain"/> once a reload has put the file into force.
    /// </summary>
    public bool FileUnreadable => _fileUnreadable;

    /// <summary>Whether there is a settings file at all.</summary>
    public bool FileExists => File.Exists(paths.SettingsFile);

    /// <summary>The settings file's full path.</summary>
    public string FilePath => paths.SettingsFile;

    /// <summary>
    /// Ends the fallback <see cref="FileUnreadable"/> describes: the file's settings are in force, or the file
    /// is gone and there is nothing left to protect. Writes are let through from here on.
    /// </summary>
    public void TrustFileAgain() => _fileUnreadable = false;

    /// <summary>
    /// Reads the settings file, creating it with defaults on first run. A malformed file is left untouched
    /// (so the user's edits aren't destroyed) and defaults are used for this session. So is a file that stays
    /// locked by another process past a short wait: reading it used to throw, which stopped SmartZoom from
    /// starting at all. Keys the current version does not know are ignored, which is how a file written by an
    /// older one keeps working.
    /// </summary>
    public SmartZoomSettings Load()
    {
        var file = paths.SettingsFile;
        if (!File.Exists(file))
        {
            var defaults = new SmartZoomSettings();
            Save(defaults);
            LogCreatedDefaults(file);
            return defaults;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var settings = Read(file);
                _fileUnreadable = false;
                LogLoaded(file);
                return settings;
            }
            catch (JsonException ex)
            {
                LogInvalidFile(ex, file);
                return new SmartZoomSettings();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < _attempts)
            {
                Thread.Sleep(_retryDelay);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogUnreadableFile(ex, file, (_attempts - 1) * _retryDelay.TotalSeconds);
                _fileUnreadable = true;
                return new SmartZoomSettings();
            }
        }
    }

    /// <summary>
    /// Reads the settings file exactly as it is, or says why it cannot be. Nothing is written and nothing is
    /// defaulted: a caller that wants the user's file, not a stand-in for it, uses this rather than
    /// <see cref="Load"/>.
    /// </summary>
    /// <param name="settings">The file's contents, when it could be read.</param>
    /// <param name="error">Why it could not be, addressed to the user.</param>
    /// <returns>False when the file is missing, unreadable or not valid JSON.</returns>
    public bool TryLoad([NotNullWhen(true)] out SmartZoomSettings? settings, [NotNullWhen(false)] out string? error) =>
        TryLoad(out settings, out error, out _, wait: false);

    /// <inheritdoc cref="TryLoad(out SmartZoomSettings?, out string?)"/>
    /// <param name="settings">The file's contents, when it could be read.</param>
    /// <param name="error">Why it could not be, addressed to the user.</param>
    /// <param name="failure">Why it could not be, for a caller that acts differently on each.</param>
    /// <param name="wait">
    /// Whether to wait out a file another process is holding, the way <see cref="Load"/> does, rather than give
    /// up at the first refusal.
    /// </param>
    public bool TryLoad(
        [NotNullWhen(true)] out SmartZoomSettings? settings,
        [NotNullWhen(false)] out string? error,
        out SettingsReadFailure failure,
        bool wait)
    {
        var file = paths.SettingsFile;
        settings = null;
        error = null;

        for (var attempt = 1; ; attempt++)
        {
            if (!File.Exists(file))
            {
                error = $"There is no settings file at {file}.";
                failure = SettingsReadFailure.Missing;
                return false;
            }

            try
            {
                // Reading the file does not end the fallback: only putting it into force does (TrustFileAgain).
                settings = Read(file);
                failure = SettingsReadFailure.None;
                return true;
            }
            catch (JsonException ex)
            {
                error = $"{file} is not valid JSON: {ex.Message}";
                failure = SettingsReadFailure.Invalid;
                return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && wait && attempt < _attempts)
            {
                Thread.Sleep(_retryDelay);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = $"{file} could not be read: {ex.Message}";
                failure = SettingsReadFailure.Unopenable;
                return false;
            }
        }
    }

    private static SmartZoomSettings Read(string file)
    {
        using var stream = File.OpenRead(file);
        return JsonSerializer.Deserialize<SmartZoomSettings>(stream, JsonOptions) ?? new SmartZoomSettings();
    }

    /// <summary>
    /// An independent copy, made the same way the file is written and read, so a settings window can be
    /// edited and thrown away without touching what the app is running.
    /// </summary>
    /// <param name="settings">The settings to copy.</param>
    public static SmartZoomSettings Clone(SmartZoomSettings settings) =>
        JsonSerializer.Deserialize<SmartZoomSettings>(JsonSerializer.Serialize(settings, JsonOptions), JsonOptions)
        ?? new SmartZoomSettings();

    /// <summary>
    /// Writes the settings as a new file, only if there is still no settings file by the time it is in place:
    /// one that appeared meanwhile is somebody's save, and it wins.
    /// </summary>
    /// <param name="settings">What to write.</param>
    /// <returns>False, with nothing changed, when a settings file was already there.</returns>
    public bool TryCreate(SmartZoomSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(paths.SettingsDirectory);
        var tempFile = paths.SettingsFile + ".tmp";
        var moved = false;
        try
        {
            File.WriteAllText(tempFile, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tempFile, paths.SettingsFile, overwrite: false);
            moved = true;
            return true;
        }
        catch (IOException) when (File.Exists(paths.SettingsFile))
        {
            return false;
        }
        finally
        {
            // Whatever stopped the move, the copy is not left lying next to the user's file.
            if (!moved)
                File.Delete(tempFile);
        }
    }

    /// <summary>Writes the settings atomically (temp file + replace) so a crash can't leave a truncated file.</summary>
    public void Save(SmartZoomSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(paths.SettingsDirectory);
        var tempFile = paths.SettingsFile + ".tmp";
        File.WriteAllText(tempFile, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tempFile, paths.SettingsFile, overwrite: true);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created default settings at {File}.")]
    private partial void LogCreatedDefaults(string file);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded settings from {File}.")]
    private partial void LogLoaded(string file);

    [LoggerMessage(Level = LogLevel.Error, Message = "Settings file {File} could not be read after waiting {Seconds:0.#} s; using defaults and leaving the file as it is until it can be read.")]
    private partial void LogUnreadableFile(Exception exception, string file, double seconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Settings file {File} is not valid JSON; using defaults for this session. Fix or delete the file.")]
    private partial void LogInvalidFile(Exception exception, string file);
}
