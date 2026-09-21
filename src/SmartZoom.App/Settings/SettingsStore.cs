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
            SmartZoomSettings settings;
            using (var stream = File.OpenRead(file))
            {
                settings = JsonSerializer.Deserialize<SmartZoomSettings>(stream, JsonOptions) ?? new SmartZoomSettings();
            }

            LogLoaded(file);
            return settings;
        }
        catch (JsonException ex)
        {
            LogInvalidFile(ex, file);
            return new SmartZoomSettings();
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Settings file {File} is not valid JSON; using defaults for this session. Fix or delete the file.")]
    private partial void LogInvalidFile(Exception exception, string file);
}
