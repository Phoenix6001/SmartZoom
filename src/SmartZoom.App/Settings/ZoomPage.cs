using Microsoft.Extensions.Logging;

using SmartZoom.Core.Settings;

namespace SmartZoom.App.Settings;

/// <summary>
/// The settings people actually turn. The measured tuning — contact spreads, wheel ticks, animation lengths —
/// stays in the file, where <c>docs/measurements.md</c> can sit next to it.
/// </summary>
internal sealed class ZoomPage : UserControl
{
    private readonly SmartZoomSettings _model;
    private readonly Action _openSettingsFile;

    private readonly CheckBox _enabled = new() { Text = "SmartZoom is &on", AutoSize = true, Name = "Enabled" };
    private readonly NumericUpDown _minScale = new() { DecimalPlaces = 2, Increment = 0.1m, Minimum = 1.01m, Maximum = 10m, Width = 90, Name = "MinScale" };
    private readonly NumericUpDown _maxScale = new() { DecimalPlaces = 2, Increment = 0.5m, Minimum = 1.01m, Maximum = 20m, Width = 90, Name = "MaxScale" };
    private readonly CheckBox _animate = new() { Text = "&Animate the zoom", AutoSize = true, Name = "Animate" };
    private readonly CheckBox _fallback = new() { Text = "&Fall back to Ctrl+wheel when a strategy can't act", AutoSize = true, Name = "Fallback" };
    private readonly ComboBox _readerMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, Name = "ReaderMode" };
    private readonly NumericUpDown _magnification = new() { DecimalPlaces = 2, Increment = 0.25m, Minimum = 1.01m, Maximum = 10m, Width = 90, Name = "Magnification" };
    private readonly ComboBox _logLevel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, Name = "LogLevel" };
    private readonly Button _openFile = new() { Text = "Open settings &file…", AutoSize = true, Name = "OpenSettingsFile" };

    /// <summary>Creates the page over the working copy.</summary>
    /// <param name="model">The settings being edited; read back by <see cref="Harvest"/>.</param>
    /// <param name="openSettingsFile">Opens the JSON file, for everything this page deliberately leaves out.</param>
    public ZoomPage(SmartZoomSettings model, Action openSettingsFile)
    {
        _model = model;
        _openSettingsFile = openSettingsFile;

        AutoScaleMode = AutoScaleMode.Dpi;
        Dock = DockStyle.Fill;
        Padding = new Padding(12);
        AutoScroll = true;

        _readerMode.Items.AddRange(["Pinch around the cursor", "The reader's own fit-width and fit-page"]);
        foreach (var level in new[] { LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error })
            _logLevel.Items.Add(level.ToString());

        _openFile.Click += (_, _) => _openSettingsFile();

        Controls.Add(BuildLayout());
        ShowSettings();
    }

    private TableLayoutPanel BuildLayout()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        Row(layout, null, _enabled);
        Separator(layout, "Zoom");
        Row(layout, "Smallest zoom worth doing", _minScale);
        Row(layout, "Largest zoom", _maxScale);
        Row(layout, null, _animate);
        Row(layout, null, _fallback);
        Separator(layout, "PDF readers");
        Row(layout, "Zoomed by", _readerMode);
        Row(layout, "Pinch magnifies by", _magnification);
        Separator(layout, "Diagnostics");
        Row(layout, "Write to the log", _logLevel);
        Row(layout, null, _openFile);

        return layout;
    }

    private static void Row(TableLayoutPanel layout, string? label, Control control)
    {
        control.Margin = new Padding(0, 4, 0, 4);
        layout.Controls.Add(new Label
        {
            Text = label ?? string.Empty,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 12, 4),
        });
        layout.Controls.Add(control);
    }

    private static void Separator(TableLayoutPanel layout, string heading)
    {
        layout.Controls.Add(new Label { Text = string.Empty, AutoSize = true });
        layout.Controls.Add(new Label
        {
            Text = heading,
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold),
            Margin = new Padding(0, 16, 0, 4),
        });
    }

    /// <summary>Puts the settings into the controls.</summary>
    private void ShowSettings()
    {
        _enabled.Checked = _model.Enabled;
        _minScale.Value = Clamp(_minScale, (decimal)_model.Zoom.MinScale);
        _maxScale.Value = Clamp(_maxScale, (decimal)_model.Zoom.MaxScale);
        _animate.Checked = _model.Zoom.Animate;
        _fallback.Checked = _model.Zoom.FallbackToCtrlWheel;
        _readerMode.SelectedIndex = _model.Zoom.Reader.Mode == ReaderZoomMode.Shortcuts ? 1 : 0;
        _magnification.Value = Clamp(_magnification, (decimal)_model.Zoom.Reader.Magnification);
        _logLevel.SelectedItem = _model.Logging.Level.ToString();
        if (_logLevel.SelectedIndex < 0)
            _logLevel.SelectedIndex = 0;
    }

    /// <summary>Reads the controls back into the settings.</summary>
    public void Harvest()
    {
        _model.Enabled = _enabled.Checked;
        _model.Zoom.MinScale = (double)_minScale.Value;
        _model.Zoom.MaxScale = (double)_maxScale.Value;
        _model.Zoom.Animate = _animate.Checked;
        _model.Zoom.FallbackToCtrlWheel = _fallback.Checked;
        _model.Zoom.Reader.Mode = _readerMode.SelectedIndex == 1 ? ReaderZoomMode.Shortcuts : ReaderZoomMode.Pinch;
        _model.Zoom.Reader.Magnification = (double)_magnification.Value;

        if (Enum.TryParse<LogLevel>(_logLevel.SelectedItem as string, out var level))
            _model.Logging.Level = level;
    }

    /// <summary>
    /// A settings file edited by hand can hold a value outside what the spinner offers. Clamping keeps the
    /// control from throwing; the validator is what tells the user the value was wrong.
    /// </summary>
    private static decimal Clamp(NumericUpDown control, decimal value) => Math.Clamp(value, control.Minimum, control.Maximum);
}
