using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.App.Diagnostics;

namespace SmartZoom.App.Tests.Diagnostics;

/// <summary>
/// SECURITY.md promises that <c>diagnostics.json</c> itself — not merely the report rendered from it —
/// holds no username. Redacting only at render time satisfied every earlier test while leaving the file on
/// disk carrying the profile path out of an <see cref="IOException"/>, which is exactly the file users are
/// invited to attach to an issue. These tests assert against the bytes that reach the disk.
/// </summary>
public sealed class RecordTimeRedactionTests
{
    private static readonly string Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string RoamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public sealed class The_stored_exception_text
    {
        [Fact]
        public void Has_the_profile_path_rewritten_before_it_is_cut_to_length()
        {
            var failing = Path.Combine(RoamingAppData, "SmartZoom", "settings.json");
            var text = DiagnosticText.ForException(
                new IOException($"The process cannot access the file '{failing}' because it is in use."));

            Assert.DoesNotContain(Profile, text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("%APPDATA%", text, StringComparison.Ordinal);
        }

        [Fact]
        public void Is_still_bounded_after_redaction()
        {
            var text = DiagnosticText.ForException(new IOException(new string('x', 20_000)));

            Assert.Contains("truncated", text, StringComparison.Ordinal);
            Assert.True(
                text.Length < DiagnosticText.MaxExceptionCharacters + 100,
                $"Expected the stored text to stay near the {DiagnosticText.MaxExceptionCharacters}-character cap, was {text.Length}.");
        }
    }

    public sealed class A_crash_written_to_disk
    {
        [Fact]
        public void Does_not_contain_the_user_profile_path()
        {
            using var temp = new TempDirectory();
            var paths = new AppPaths(SettingsDirectory: temp.Path, LogDirectory: Path.Combine(temp.Path, "logs"));
            var recorder = new DiagnosticRecorder(
                new DiagnosticStore(paths, NullLogger<DiagnosticStore>.Instance),
                TimeProvider.System,
                version: "0.1.0-test");

            var failing = Path.Combine(RoamingAppData, "SmartZoom", "settings.json");
            Crash.Record(recorder, new IOException($"Could not write '{failing}'."));

            // The raw file, not the loaded record: what a user attaches to an issue is these bytes.
            var stored = File.ReadAllText(paths.DiagnosticsFile);
            Assert.DoesNotContain(Profile, stored, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Profile.Replace(@"\", @"\\", StringComparison.Ordinal), stored, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("%APPDATA%", stored, StringComparison.Ordinal);
        }
    }
}
