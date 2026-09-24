using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using SmartZoom.Core.Settings;

namespace SmartZoom.App.Settings;

/// <summary>Loads and saves <see cref="SmartZoomSettings"/> as human-editable JSON.</summary>
internal sealed partial class SettingsStore(AppPaths paths, ILogger<SettingsStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Reads the settings file, creating it with defaults on first run. A malformed file is left untouched
    /// (so the user's edits aren't destroyed) and defaults are used for this session. Keys the current
    /// version does not know are ignored, which is how a file written by an older one keeps working.
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

        try
        {
            var settings = Read(file);
            LogLoaded(file);
            return settings;
        }
        catch (JsonException ex)
        {
            LogInvalidFile(ex, file);
            return new SmartZoomSettings();
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
    public bool TryLoad([NotNullWhen(true)] out SmartZoomSettings? settings, [NotNullWhen(false)] out string? error)
    {
        var file = paths.SettingsFile;
        settings = null;
        error = null;

        if (!File.Exists(file))
        {
            error = $"There is no settings file at {file}.";
            return false;
        }

        try
        {
            settings = Read(file);
            return true;
        }
        catch (JsonException ex)
        {
            error = $"{file} is not valid JSON: {ex.Message}";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"{file} could not be read: {ex.Message}";
            return false;
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Settings file {File} is not valid JSON; using defaults for this session. Fix or delete the file.")]
    private partial void LogInvalidFile(Exception exception, string file);
}
