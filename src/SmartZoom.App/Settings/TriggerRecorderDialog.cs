using SmartZoom.Core.Input;
using SmartZoom.Core.Settings;

namespace SmartZoom.App.Settings;

/// <summary>
/// Asks the user to perform the gesture they want, rather than to name it. The one thing everybody has to
/// configure is which button starts a zoom, and "press it" is the only instruction that needs no knowledge of
/// what Windows calls the button behind the scroll wheel.
/// </summary>
/// <remarks>
/// SmartZoom's own trigger is switched off for as long as this is open. Otherwise pressing the button you are
/// trying to record would zoom the settings window.
/// </remarks>
internal sealed class TriggerRecorderDialog : Form
{
    private readonly ITriggerSource _triggers;
    private readonly bool _triggersWereEnabled;

    private readonly Label _captured = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 12f, FontStyle.Bold),
        Text = "Press a button or key combination…",
    };

    private readonly RadioButton _everyPress = new() { Text = "&Every press", AutoSize = true };
    private readonly RadioButton _doubleTap = new() { Text = "&Double-tap", AutoSize = true };
    private readonly NumericUpDown _window = new() { Minimum = 1, Maximum = 5000, Increment = 50, Width = 90 };
    private readonly Label _windowLabel = new() { Text = "within", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _windowUnit = new() { Text = "ms", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly CheckBox _swallow = new() { Text = "&Hide the press from the application", AutoSize = true };
    private readonly Button _ok = new() { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, Enabled = false };
    private readonly Button _cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };

    private MouseButton? _button;
    private KeyCombo? _combo;

    /// <summary>Opens the recorder, optionally on an existing trigger.</summary>
    /// <param name="triggers">Silenced while the dialog is open.</param>
    /// <param name="systemDoubleClickMs">The default double-tap window, when the trigger does not set one.</param>
    /// <param name="existing">The trigger being edited, or null to record a new one.</param>
    public TriggerRecorderDialog(ITriggerSource triggers, uint systemDoubleClickMs, TriggerSettings? existing)
    {
        _triggers = triggers;

        // Silenced rather than stopped: stopping the hook would drop the user's other triggers too, and this
        // dialog is exactly where they are most likely to press the one that is already configured.
        _triggersWereEnabled = triggers.Enabled;
        triggers.Enabled = false;

        Text = existing is null ? "New trigger" : "Edit trigger";
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        AcceptButton = _ok;
        CancelButton = _cancel;
        ClientSize = new Size(440, 260);
        Padding = new Padding(12);

        _window.Value = systemDoubleClickMs;
        _everyPress.Checked = true;
        _everyPress.CheckedChanged += (_, _) => UpdateWindowEnabled();
        _captured.MouseDown += OnCaptureMouseDown;
        KeyDown += OnCaptureKeyDown;

        if (existing is not null)
            Restore(existing, systemDoubleClickMs);

        Controls.Add(BuildLayout());
        UpdateWindowEnabled();
    }

    /// <summary>What was recorded. Only meaningful after <see cref="DialogResult.OK"/>.</summary>
    public TriggerSettings Result => new()
    {
        Mouse = _button,
        Keys = _combo?.ToString(),
        TapCount = _doubleTap.Checked ? 2 : 1,
        DoubleTapWindowMs = _doubleTap.Checked ? (uint)_window.Value : null,
        SwallowClicks = _swallow.Checked,
    };

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _triggers.Enabled = _triggersWereEnabled;

        base.Dispose(disposing);
    }

    private TableLayoutPanel BuildLayout()
    {
        var taps = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        taps.Controls.AddRange([_everyPress, _doubleTap, _windowLabel, _window, _windowUnit]);
        foreach (Control control in taps.Controls)
            control.Margin = new Padding(0, 6, 8, 0);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange([_cancel, _ok]);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = "Press the mouse button or key combination you want to use. Mouse buttons other than the middle "
                + "and side buttons are left alone, so SmartZoom can never take over a normal click.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 56,
            Margin = new Padding(0, 0, 0, 8),
        });
        layout.Controls.Add(_captured);
        layout.Controls.Add(taps);
        layout.Controls.Add(_swallow);
        layout.Controls.Add(buttons);
        return layout;
    }

    private void Restore(TriggerSettings existing, uint systemDoubleClickMs)
    {
        _button = existing.Mouse;
        if (KeyCombo.TryParse(existing.Keys, out var combo))
            _combo = combo;

        _doubleTap.Checked = existing.TapCount == 2;
        _everyPress.Checked = existing.TapCount != 2;
        _window.Value = existing.DoubleTapWindowMs ?? systemDoubleClickMs;
        _swallow.Checked = existing.SwallowClicks;
        Show(Describe());
    }

    private void OnCaptureMouseDown(object? sender, MouseEventArgs e)
    {
        var button = e.Button switch
        {
            MouseButtons.Middle => MouseButton.Middle,
            MouseButtons.XButton1 => MouseButton.XButton1,
            MouseButtons.XButton2 => MouseButton.XButton2,
            _ => MouseButton.None,
        };

        if (button == MouseButton.None)
        {
            // Left and right are deliberately not available: a global hook that can delay or swallow the
            // primary buttons would make the machine unusable when SmartZoom misbehaves.
            Show("That button can't be a trigger. Try the wheel click or a side button.");
            return;
        }

        _button = button;
        _combo = null;
        Show(Describe());
    }

    private void OnCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        // Nothing typed here should reach a control; the dialog's own buttons are reached with the mouse.
        e.Handled = true;
        e.SuppressKeyPress = true;

        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            return;
        }

        var key = VirtualKeys.Fold((ushort)e.KeyValue);
        var modifiers = Modifiers(e);

        // A bare modifier is a legitimate trigger ("double-tap Ctrl"), so it is not filtered out; it just
        // must not appear both as the key and as one of the modifiers it is held with.
        if (VirtualKeys.TryGetModifier(key, out var self))
            modifiers &= ~self;

        var combo = new KeyCombo(modifiers, key);
        if (!KeyCombo.TryParse(combo.ToString(), out _))
        {
            // Media keys, IME keys and the like have no name the settings file could round-trip.
            Show("That key can't be written down, so it can't be a trigger.");
            return;
        }

        _combo = combo;
        _button = null;
        Show(Describe());
    }

    private static KeyModifiers Modifiers(KeyEventArgs e)
    {
        var modifiers = KeyModifiers.None;
        if (e.Control)
            modifiers |= KeyModifiers.Control;
        if (e.Alt)
            modifiers |= KeyModifiers.Alt;
        if (e.Shift)
            modifiers |= KeyModifiers.Shift;

        // WinForms has no Win flag; the key state is the only way to see it.
        if ((Control.ModifierKeys & Keys.LWin) != 0 || (Control.ModifierKeys & Keys.RWin) != 0)
            modifiers |= KeyModifiers.Win;

        return modifiers;
    }

    private string Describe() => _button is { } button ? button.ToString() : _combo?.ToString() ?? string.Empty;

    private void Show(string text)
    {
        _captured.Text = text;
        _ok.Enabled = _button is not null || _combo is not null;
    }

    private void UpdateWindowEnabled()
    {
        _windowLabel.Enabled = _doubleTap.Checked;
        _window.Enabled = _doubleTap.Checked;
        _windowUnit.Enabled = _doubleTap.Checked;
    }
}
