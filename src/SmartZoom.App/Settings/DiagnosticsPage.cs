using System.Globalization;

using SmartZoom.App.Diagnostics;
using SmartZoom.Core.Diagnostics;
using SmartZoom.Core.Settings;

namespace SmartZoom.App.Settings;

/// <summary>
/// Everything SmartZoom would tell you about itself, rendered as text a person can read before they decide
/// to paste it anywhere. Nothing here is ever sent — showing the report to the person who owns the machine
/// is the whole consent mechanism, which is also why the tray's "Diagnostic report…" item opens this page
/// instead of copying anything silently.
/// </summary>
internal sealed class DiagnosticsPage : UserControl
{
    /// <summary>How many lines from the end of the log the report carries when asked to.</summary>
    private const int LogTailLines = 200;

    private readonly DiagnosticRecorder _recorder;
    private readonly DiagnosticsSettings _model;
    private readonly IMachineFacts _facts;
    private readonly AppPaths _paths;

    // Owned here and disposed here: a control does not dispose a Font it was handed.
    private readonly Font _monospace = new("Consolas", 9f);

    private readonly TextBox _report = new()
    {
        Multiline = true,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Name = "Report",
    };

    private readonly CheckBox _enabled = new() { Text = "&Record what doesn't work", AutoSize = true, Name = "RecordEnabled" };
    private readonly CheckBox _includeLog = new() { Text = "Include recent &log lines", AutoSize = true, Name = "IncludeLog" };
    private readonly Button _refresh = new() { Text = "&Refresh", AutoSize = true, Name = "Refresh" };
    private readonly Button _copy = new() { Text = "&Copy", AutoSize = true, Name = "Copy" };
    private readonly Button _save = new() { Text = "&Save…", AutoSize = true, Name = "SaveReport" };
    private readonly Button _clear = new() { Text = "Clear recorded &data", AutoSize = true, Name = "ClearData" };

    // What Copy or Save actually did — a failed clipboard or file write must never look identical to a
    // successful one, since reading this page IS the consent step: the user has to be able to tell whether
    // the thing they are about to paste actually made it anywhere.
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        Height = 20,
        AutoEllipsis = true,
        Margin = new Padding(0, 4, 0, 0),
        Name = "Status",
    };

    /// <summary>Creates the page over the live recorder.</summary>
    /// <param name="recorder">Only ever read through <see cref="DiagnosticRecorder.Snapshot"/>: it may be
    /// written from the dispatcher thread at any moment, and enumerating it live would throw.</param>
    /// <param name="model">The working copy of the settings; the record switch is edited here and applied on Save.</param>
    /// <param name="facts">What this machine is.</param>
    /// <param name="paths">Where the settings file and the logs live.</param>
    public DiagnosticsPage(DiagnosticRecorder recorder, DiagnosticsSettings model, IMachineFacts facts, AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(paths);

        _recorder = recorder;
        _model = model;
        _facts = facts;
        _paths = paths;
        _report.Font = _monospace;

        AutoScaleMode = AutoScaleMode.Dpi;
        Dock = DockStyle.Fill;
        Padding = new Padding(12);

        // Through the working settings copy, like every other page: the switch then takes effect on Save and
        // is undone by Close, instead of being a live mutation of the recorder that no button can cancel.
        _enabled.Checked = _model.Enabled;
        _enabled.CheckedChanged += (_, _) => _model.Enabled = _enabled.Checked;
        _includeLog.CheckedChanged += async (_, _) => await RenderAsync(announce: false).ConfigureAwait(true);
        _refresh.Click += async (_, _) => await RenderAsync(announce: true).ConfigureAwait(true);
        _copy.Click += (_, _) => Copy();
        _save.Click += (_, _) => Save();
        _clear.Click += async (_, _) =>
        {
            recorder.Clear();
            await RenderAsync(announce: false).ConfigureAwait(true);
        };

        Controls.Add(BuildLayout());
        _ = RenderAsync(announce: false);
    }

    private TableLayoutPanel BuildLayout()
    {
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        buttons.Controls.AddRange([_refresh, _copy, _save, _clear, _enabled, _includeLog]);
        foreach (Control control in buttons.Controls)
            control.Margin = new Padding(0, 6, 8, 0);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
        layout.Controls.Add(_status);
        return layout;
    }

    /// <summary>Guarded because a clipboard held open by another process must not become a recorded crash.</summary>
    private void Copy()
    {
        try
        {
            Clipboard.SetText(_report.Text);
            ShowStatus("Copied to the clipboard.", failed: false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowStatus($"Couldn't copy to the clipboard: {ex.Message}", failed: true);
        }
    }

    /// <summary>Guarded for the same reason as <see cref="Copy"/>: a bad path or permission error must not
    /// reach the app-wide handler and be recorded as a crash.</summary>
    private void Save()
    {
        using var dialog = new SaveFileDialog { FileName = "smartzoom-diagnostics.md", Filter = "Markdown|*.md" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            File.WriteAllText(dialog.FileName, _report.Text);
            ShowStatus($"Saved to {dialog.FileName}.", failed: false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowStatus($"Couldn't save: {ex.Message}", failed: true);
        }
    }

    private void ShowStatus(string text, bool failed)
    {
        _status.Text = text;
        _status.ForeColor = failed ? Palette.ErrorText : SystemColors.GrayText;
    }

    /// <summary>Rebuilds the report from the record as it stands now, and says when it was built.</summary>
    public void RenderReport() => _ = RenderAsync(announce: true);

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _monospace.Dispose();

        base.Dispose(disposing);
    }

    /// <summary>
    /// Builds the report off the UI thread: the log tail and the settings file are disk reads, and the
    /// display facts are hardware queries. The buttons that act on the report wait until it is there.
    /// </summary>
    /// <param name="announce">Whether to put the build time in the status line.</param>
    private async Task RenderAsync(bool announce)
    {
        var includeLog = _includeLog.Checked;
        SetBusy(true);
        try
        {
            var text = await Task.Run(() => Build(includeLog)).ConfigureAwait(true);
            if (IsDisposed)
                return;

            _report.Text = text;
            if (announce)
                ShowStatus($"Report built at {DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture)}.", failed: false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!IsDisposed)
                ShowStatus($"Couldn't build the report: {ex.Message}", failed: true);
        }
        finally
        {
            if (!IsDisposed)
                SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _refresh.Enabled = !busy;
        _copy.Enabled = !busy;
        _save.Enabled = !busy;
        _clear.Enabled = !busy;
        _includeLog.Enabled = !busy; // its CheckedChanged starts a render; two builds would race for the report text
    }

    /// <summary>Everything that reads a file or the hardware; runs on a thread-pool thread.</summary>
    private string Build(bool includeLog)
    {
        var tail = includeLog ? LogTailOrNull() : null;
        return DiagnosticReport.Render(_recorder.Snapshot(), _facts, ReadSettingsJson(), tail, Redact);
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

    /// <summary>The end of the newest log file, or null when the log can't be read for any reason.</summary>
    private string? LogTailOrNull()
    {
        try
        {
            var newest = Directory.EnumerateFiles(_paths.LogDirectory, "*.log")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();

            return newest is null ? null : LogTail.ReadLast(newest.FullName, LogTailLines);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static string Redact(string text) => DiagnosticText.Redact(text);
}
