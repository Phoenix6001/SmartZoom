using System.Text;

namespace SmartZoom.App.Diagnostics;

/// <summary>Reads the end of a log file without reading the rest of it.</summary>
/// <remarks>
/// Serilog is still writing today's file while this reads it, and a file opened with only
/// <see cref="FileShare.Read"/> cannot be opened at all while another handle holds it for writing, so the
/// share mode has to admit the writer too.
/// </remarks>
internal static class LogTail
{
    private const int BlockSize = 16 * 1024;

    /// <summary>The last <paramref name="lineCount"/> lines of the file, joined with the platform newline.</summary>
    /// <param name="path">The file to read.</param>
    /// <param name="lineCount">How many lines from the end to keep.</param>
    /// <exception cref="IOException">The file could not be read.</exception>
    public static string ReadLast(string path, int lineCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineCount);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        // Blocks are read from the end until they hold one newline more than the lines wanted: the first
        // block's opening text is a line cut in the middle and is dropped, so one extra newline pays for it.
        var position = stream.Length;
        var newlines = 0;
        var blocks = new List<byte[]>();
        while (position > 0 && newlines <= lineCount)
        {
            var size = (int)Math.Min(BlockSize, position);
            position -= size;
            var block = new byte[size];
            stream.Position = position;
            stream.ReadExactly(block);
            newlines += block.AsSpan().Count((byte)'\n');
            blocks.Add(block);
        }

        blocks.Reverse();
        var text = Encoding.UTF8.GetString(blocks.SelectMany(b => b).ToArray());
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();

        if (position > 0)
            lines.RemoveAt(0);

        // A file that ends in a newline has no empty last line, only a complete one.
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        return string.Join(Environment.NewLine, lines.Count <= lineCount ? lines : lines[^lineCount..]);
    }
}
