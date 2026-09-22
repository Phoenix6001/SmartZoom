using SmartZoom.App.Diagnostics;
using SmartZoom.Core.Diagnostics;

namespace SmartZoom.App.Settings;

/// <summary>
/// Everything SmartZoom would tell you about itself, rendered as text a person can read before they decide
/// to paste it anywhere. Nothing here is ever sent — showing the report to the person who owns the machine
/// is the whole consent mechanism, which is also why the tray's "Diagnostic report…" item opens this page
/// instead of copying anything silently.
/// </summary>
internal sealed class DiagnosticsPage : UserControl
{
    private readonly DiagnosticRecorder _recorder;
    private readonly IMachineFacts _facts;
    private readonly AppPaths _paths;

    private readonly TextBox _report = new()
    {
        Multiline = true,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font("Consolas", 9f),
        Name = "Report",
    };

    private readonly CheckBox _enabled = new() { Text = "&Record what doesn't work", AutoSize = true, Name = "RecordEnabled" };
    private readonly CheckBox _includeLog = new() { Text = "Include recent &log lines", AutoSize = true, Name = "IncludeLog" };
    private readonly Button _copy = new() { Text = "&Copy", AutoSize = true, Name = "Copy" };
    private readonly Button _save = new() { Text = "&Save…", AutoSize = true, Name = "SaveReport" };
    private readonly Button _clear = new() { Text = "Clear recorded &data", AutoSize = true, Name = "ClearData" };

    /// <summary>Creates the page over the live recorder.</summary>
    /// <param name="recorder">Only ever read through <see cref="DiagnosticRecorder.Snapshot"/>: it may be
    /// written from the dispatcher thread at any moment, and enumerating it live would throw.</param>
    /// <param name="facts">What this machine is.</param>
    /// <param name="paths">Where the settings file and the logs live.</param>
    public DiagnosticsPage(DiagnosticRecorder recorder, IMachineFacts facts, AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(paths);

        _recorder = recorder;
        _facts = facts;
        _paths = paths;

        AutoScaleMode = AutoScaleMode.Dpi;
        Dock = DockStyle.Fill;
        Padding = new Padding(12);

        _enabled.Checked = recorder.Enabled;
        _enabled.CheckedChanged += (_, _) => recorder.Enabled = _enabled.Checked;
        _includeLog.CheckedChanged += (_, _) => Render();
        _copy.Click += (_, _) => Clipboard.SetText(_report.Text);
        _save.Click += (_, _) => Save();
        _clear.Click += (_, _) => { recorder.Clear(); Render(); };

        Controls.Add(BuildLayout());
        Render();
    }

    private TableLayoutPanel BuildLayout()
    {
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        buttons.Controls.AddRange([_copy, _save, _clear, _enabled, _includeLog]);
        foreach (Control control in buttons.Controls)
            control.Margin = new Padding(0, 6, 8, 0);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = "This is everything SmartZoom would tell you about itself. Read it, then copy it into a "
                + "bug report. Nothing here is sent anywhere.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 36,
        });
        layout.Controls.Add(_report);
        layout.Controls.Add(buttons);
        return layout;
    }

    private void Save()
    {
        using var dialog = new SaveFileDialog { FileName = "smartzoom-diagnostics.md", Filter = "Markdown|*.md" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            File.WriteAllText(dialog.FileName, _report.Text);
    }

    private void Render()
    {
        var tail = _includeLog.Checked ? LogTail() : null;
        _report.Text = DiagnosticReport.Render(_recorder.Snapshot(), _facts, ReadSettingsJson(), tail, Redact);
    }

    /// <summary>Reads the settings file for the report. A missing or locked file yields an empty object, not a crash.</summary>
    private string ReadSettingsJson()
    {
        try
        {
            return File.Exists(_paths.SettingsFile) ? File.ReadAllText(_paths.SettingsFile) : "{}";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return "{}";
        }
    }

    /// <summary>The last 200 lines of the newest log file, or null when the log can't be read for any reason.</summary>
    private string? LogTail()
    {
        try
        {
            var newest = Directory.EnumerateFiles(_paths.LogDirectory, "*.log")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
            if (newest is null)
                return null;

            // The newest file is usually today's, which this same process is actively writing to via Serilog.
            // File.ReadAllLines opens with FileShare.Read only, which is not enough to open a file another
            // handle already holds for writing: the writer's own access must be permitted by this open's
            // share flags too, not just the other way around. FileShare.ReadWrite grants that without asking
            // for write access itself.
            using var stream = new FileStream(newest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
                lines.Add(line);

            return string.Join(Environment.NewLine, lines.Count <= 200 ? lines : lines[^200..]);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static string Redact(string text) => Redaction.Paths(
        text,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
}
