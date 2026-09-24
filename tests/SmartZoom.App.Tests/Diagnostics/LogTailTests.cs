using System.Text;

using SmartZoom.App.Diagnostics;

namespace SmartZoom.App.Tests.Diagnostics;

/// <summary>
/// The report's log excerpt. Read from the end of the file, so the size of a day's log has no bearing on
/// how long the Diagnostics page takes to build, and while Serilog still holds the file open for writing.
/// </summary>
public sealed class LogTailTests
{
    private static string Line(int n) => $"2026-09-23 10:00:{n % 60:00}.000 +02:00 [INF] Line {n}";

    private static string Write(TempDirectory temp, IEnumerable<string> lines, string newline = "\r\n", bool trailingNewline = true)
    {
        var path = Path.Combine(temp.Path, "smartzoom-20260923.log");
        var text = string.Join(newline, lines) + (trailingNewline ? newline : string.Empty);
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    public sealed class The_last_lines_of_a_file
    {
        [Fact]
        public void Are_exactly_the_last_N_lines_with_the_line_endings_taken_off()
        {
            using var temp = new TempDirectory();
            var path = Write(temp, Enumerable.Range(1, 50).Select(Line));

            var tail = LogTail.ReadLast(path, 3);

            Assert.Equal(string.Join(Environment.NewLine, [Line(48), Line(49), Line(50)]), tail);
        }

        [Fact]
        public void Include_a_last_line_that_has_no_newline_after_it()
        {
            using var temp = new TempDirectory();
            var path = Write(temp, Enumerable.Range(1, 10).Select(Line), trailingNewline: false);

            var tail = LogTail.ReadLast(path, 2);

            Assert.Equal(string.Join(Environment.NewLine, [Line(9), Line(10)]), tail);
        }

        [Fact]
        public void Are_whole_lines_even_when_the_file_is_far_larger_than_one_read_block()
        {
            // 4 000 lines of ~50 bytes is around 200 KB: the tail has to be assembled from more than one block
            // read backwards from the end, and the partial line at the start of the earliest block dropped.
            using var temp = new TempDirectory();
            var path = Write(temp, Enumerable.Range(1, 4_000).Select(Line));

            var tail = LogTail.ReadLast(path, 400);

            var lines = tail.Split(Environment.NewLine);
            Assert.Equal(400, lines.Length);
            Assert.Equal(Line(3_601), lines[0]);
            Assert.Equal(Line(4_000), lines[^1]);
        }
    }

    public sealed class A_file_shorter_than_asked_for
    {
        [Fact]
        public void Comes_back_whole()
        {
            using var temp = new TempDirectory();
            var path = Write(temp, [Line(1), Line(2)]);

            var tail = LogTail.ReadLast(path, 200);

            Assert.Equal(string.Join(Environment.NewLine, [Line(1), Line(2)]), tail);
        }
    }

    public sealed class An_empty_file
    {
        [Fact]
        public void Yields_an_empty_string()
        {
            using var temp = new TempDirectory();
            var path = Write(temp, [], trailingNewline: false);

            Assert.Equal(string.Empty, LogTail.ReadLast(path, 200));
        }
    }

    public sealed class A_file_another_process_is_writing
    {
        [Fact]
        public void Can_still_be_read()
        {
            // Serilog's shared sink keeps today's file open for writing; a reader that asked for exclusive
            // access, as File.ReadAllText does, would fail with a sharing violation exactly when the report
            // is most wanted.
            using var temp = new TempDirectory();
            var path = Write(temp, Enumerable.Range(1, 5).Select(Line));
            using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);

            Assert.Throws<IOException>(() => File.ReadAllText(path));
            Assert.Equal(Line(5), LogTail.ReadLast(path, 1));
        }
    }

    public sealed class The_line_count
    {
        [Fact]
        public void Must_be_positive()
        {
            using var temp = new TempDirectory();
            var path = Write(temp, [Line(1)]);

            Assert.Throws<ArgumentOutOfRangeException>(() => LogTail.ReadLast(path, 0));
        }
    }
}
