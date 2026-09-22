using SmartZoom.Core.Diagnostics;

namespace SmartZoom.Core.Tests.Diagnostics;

public class RedactionTests
{
    private const string Home = @"C:\Users\ada";
    private const string Local = @"C:\Users\ada\AppData\Local";
    private const string Roaming = @"C:\Users\ada\AppData\Roaming";

    private static string Redact(string text) => Redaction.Paths(text, Home, Local, Roaming);

    public sealed class A_path_under_the_profile
    {
        [Fact]
        public void Becomes_an_environment_variable()
        {
            Assert.Equal(@"%LOCALAPPDATA%\SmartZoom\logs", Redact(@"C:\Users\ada\AppData\Local\SmartZoom\logs"));
            Assert.Equal(@"%APPDATA%\SmartZoom\settings.json", Redact(@"C:\Users\ada\AppData\Roaming\SmartZoom\settings.json"));
            Assert.Equal(@"%USERPROFILE%\Documents\tax.pdf", Redact(@"C:\Users\ada\Documents\tax.pdf"));
        }

        [Fact]
        public void Is_replaced_inside_a_longer_sentence()
        {
            var message = @"Could not access 'C:\Users\ada\Documents\tax.pdf' because it is in use.";

            Assert.DoesNotContain("ada", Redact(message), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Is_matched_whatever_its_casing()
        {
            Assert.Equal(@"%USERPROFILE%\x", Redact(@"c:\users\ADA\x"));
        }

        [Fact]
        public void Forward_slash_paths_are_redacted()
        {
            Assert.Equal(@"%LOCALAPPDATA%/SmartZoom/logs", Redact(@"C:/Users/ada/AppData/Local/SmartZoom/logs"));
            Assert.Equal(@"%USERPROFILE%/Documents/tax.pdf", Redact(@"C:/Users/ada/Documents/tax.pdf"));
        }

        [Fact]
        public void Doubled_backslash_paths_are_redacted()
        {
            // JSON representation of paths with doubled backslashes
            var jsonConfig = @"{""\LogDirectory\"":\""C:\\Users\\ada\\AppData\\Local\\SmartZoom\""}";
            var result = Redact(jsonConfig);
            Assert.DoesNotContain("ada", result, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("%LOCALAPPDATA%", result);
        }

        [Fact]
        public void Doubled_backslash_longest_folder_is_replaced_first()
        {
            // Verify that the doubled LocalAppData is replaced before the doubled Home path
            var jsonWithBoth = @"C:\\Users\\ada\\AppData\\Local\\SmartZoom\\ and C:\\Users\\ada\\Documents\\";
            var result = Redact(jsonWithBoth);

            // The longer path should be replaced first, so LocalAppData is replaced before home
            Assert.Contains("%LOCALAPPDATA%", result);
            Assert.Contains("%USERPROFILE%", result);
            Assert.DoesNotContain("ada", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Empty_folder_arguments_are_tolerated()
        {
            // Should not crash when folders are empty
            var result = Redaction.Paths(@"C:\Users\ada\Documents\file.txt", "", "", "");
            Assert.Equal(@"C:\Users\ada\Documents\file.txt", result);
        }
    }

    public sealed class Text_longer_than_the_limit
    {
        [Fact]
        public void Is_cut_and_marked()
        {
            var cut = Redaction.Truncate(new string('x', 50), 10);

            Assert.StartsWith("xxxxxxxxxx", cut, StringComparison.Ordinal);
            Assert.Contains("truncated", cut, StringComparison.Ordinal);
        }

        [Fact]
        public void Shorter_text_is_untouched()
        {
            Assert.Equal("short", Redaction.Truncate("short", 10));
        }

        [Fact]
        public void Text_exactly_at_the_limit_is_untouched()
        {
            Assert.Equal("short", Redaction.Truncate("short", 5));
        }
    }
}
