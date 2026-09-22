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
    }
}
