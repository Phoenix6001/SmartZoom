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
internal sealed class TriggerRecorderDialog : Form, IMessageFilter
{
    // One for every dialog rather than one per dialog: a control does not dispose a Font it was handed.
    private static readonly Font CapturedFont = new(SystemFonts.MessageBoxFont!.FontFamily, 12f, FontStyle.Bold);

    private readonly ITriggerSource? _triggers;
    private readonly bool _triggersWereEnabled;

    private readonly Label _captured = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        BorderStyle = BorderStyle.FixedSingle,
        Font = CapturedFont,
        Text = "Press a button or key combination…",
    };

    private readonly Label _presetsLabel = new() { Text = "Or pick a common one:", AutoSize = true };
    private readonly RadioButton _everyPress = new() { Text = "&Every press", AutoSize = true };
    private readonly RadioButton _doubleTap = new() { Text = "&Double-tap", AutoSize = true };
    private readonly NumericUpDown _window = new() { Minimum = 1, Maximum = 5000, Increment = 50, Width = 90 };
    private readonly Label _windowLabel = new() { Text = "within", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _windowUnit = new() { Text = "ms", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly CheckBox _swallow = new() { Text = "&Hide the press from the application", AutoSize = true };
    private readonly Button _ok = new() { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, Enabled = false };
    private readonly Button _cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };

    private MouseButton? _button;
    private KeyModifiers _modifiers;
    private KeyCombo? _combo;

    /// <summary>
    /// A modifier that is down but not yet recorded. A held modifier can be the start of a modifier + click
    /// trigger or a trigger on its own ("double-tap Ctrl"); which one is known only when the next thing
    /// happens: a click while it is held, or its release.
    /// </summary>
    private KeyCombo? _heldModifier;

    /// <summary>
    /// The handful of triggers most people settle on, as one click each. Pressing your own is still the point of
    /// this dialog; these are here because a side button has no name anybody recognises, and on an unfamiliar
    /// mouse "the forward side button" is easier to pick from a list than to find and press.
    /// </summary>
    /// <remarks>
    /// Each preset carries its own tap and swallow policy, because they are not interchangeable. A dedicated side
    /// button is best hidden from the application underneath, and with a single press there is no delay in doing
    /// so. The wheel button already means "open in a new tab" and "autoscroll", so it needs the double-tap and
    /// must NOT be swallowed — swallowing would hold every middle click back by the whole double-tap window.
    /// </remarks>
    private static readonly (string Label, TriggerSettings Trigger)[] Presets =
    [
        ("Forward side button", new TriggerSettings { Mouse = MouseButton.XButton2, TapCount = 1, SwallowClicks = true }),
        ("Back side button", new TriggerSettings { Mouse = MouseButton.XButton1, TapCount = 1, SwallowClicks = true }),
        ("Double-click the wheel", new TriggerSettings { Mouse = MouseButton.Middle, TapCount = 2, SwallowClicks = false }),
        ("Ctrl+Alt+Z", new TriggerSettings { Keys = "Ctrl+Alt+Z", TapCount = 1, SwallowClicks = true }),
    ];

    /// <summary>Opens the recorder, optionally on an existing trigger.</summary>
    /// <param name="triggers">
    /// Silenced while the dialog is open. Null when nothing is running to silence — the installer shows this
    /// before SmartZoom has ever started.
    /// </param>
    /// <param name="systemDoubleClickMs">The default double-tap window, when the trigger does not set one.</param>
    /// <param name="existing">The trigger being edited, or null to record a new one.</param>
    public TriggerRecorderDialog(ITriggerSource? triggers, uint systemDoubleClickMs, TriggerSettings? existing)
    {
        _triggers = triggers;

        // Silenced rather than stopped: stopping the hook would drop the user's other triggers too, and this
        // dialog is exactly where they are most likely to press the one that is already configured.
        _triggersWereEnabled = triggers?.Enabled ?? false;
        if (triggers is not null)
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
        // Wide enough for the four presets to sit on one row at 100% scaling; they wrap rather than clip if a
        // display, a font size or a translation makes them wider.
        ClientSize = new Size(540, 330);
        Padding = new Padding(12);

        _window.Value = systemDoubleClickMs;
        _everyPress.Checked = true;
        _everyPress.CheckedChanged += (_, _) => UpdateWindowEnabled();
        // Mouse presses are taken from the message stream rather than from one control, so a click anywhere on the
        // dialog counts except on the controls that are meant to be clicked (the buttons, the radios, the spinner).
        Application.AddMessageFilter(this);
        KeyDown += OnCaptureKeyDown;
        KeyUp += OnCaptureKeyUp;

        if (existing is not null)
            Restore(existing, systemDoubleClickMs);

        Controls.Add(BuildLayout());
        UpdateWindowEnabled();
    }

    /// <summary>What was recorded. Only meaningful after <see cref="DialogResult.OK"/>.</summary>
    public TriggerSettings Result =>
        Compose(_button, _modifiers, _combo, _doubleTap.Checked, (uint)_window.Value, _swallow.Checked);

    /// <summary>
    /// The settings entry for what was recorded. Modifiers are written only for a mouse trigger that has them;
    /// a hotkey carries its own inside <see cref="TriggerSettings.Keys"/>.
    /// </summary>
    /// <param name="button">The recorded button, or null for a hotkey.</param>
    /// <param name="modifiers">The modifiers held with the button.</param>
    /// <param name="combo">The recorded combination, or null for a mouse trigger.</param>
    /// <param name="doubleTap">Whether two presses make a trigger.</param>
    /// <param name="windowMs">The double-tap window; only written for a double tap.</param>
    /// <param name="swallow">Whether to hide the press from the application.</param>
    internal static TriggerSettings Compose(MouseButton? button, KeyModifiers modifiers, KeyCombo? combo, bool doubleTap, uint windowMs, bool swallow) => new()
    {
        Mouse = button,
        Modifiers = button is not null && modifiers != KeyModifiers.None ? KeyCombo.FormatModifiers(modifiers) : null,
        Keys = combo?.ToString(),
        TapCount = doubleTap ? 2 : 1,
        DoubleTapWindowMs = doubleTap ? windowMs : null,
        SwallowClicks = swallow,
    };

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.RemoveMessageFilter(this);
            if (_triggers is not null)
                _triggers.Enabled = _triggersWereEnabled;
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    bool IMessageFilter.PreFilterMessage(ref Message m)
    {
        const int WmKeyDown = 0x0100, WmSysKeyDown = 0x0104;
        const int WmLButtonDown = 0x0201, WmRButtonDown = 0x0204, WmMButtonDown = 0x0207, WmXButtonDown = 0x020B;

        // Bit 30 of a key-down message's lParam says the key was already down: the keyboard's auto-repeat. A held
        // modifier repeats every few tens of milliseconds, and each repeat would otherwise look like a new press
        // and turn a click that was just recorded back into "holding Ctrl".
        if (m.Msg is WmKeyDown or WmSysKeyDown)
            return ((long)m.LParam & (1L << 30)) != 0;

        if (m.Msg is not (WmLButtonDown or WmRButtonDown or WmMButtonDown or WmXButtonDown))
            return false;

        var control = FromHandle(m.HWnd);
        if (control is null || control.FindForm() != this || control is ButtonBase or NumericUpDown or TextBoxBase)
            return false;

        var button = m.Msg switch
        {
            WmLButtonDown => MouseButtons.Left,
            WmRButtonDown => MouseButtons.Right,
            WmMButtonDown => MouseButtons.Middle,
            _ => ((long)m.WParam >> 16) == 1 ? MouseButtons.XButton1 : MouseButtons.XButton2,
        };

        OnCaptureMouseDown(this, new MouseEventArgs(button, 1, 0, 0, 0));
        return true;
    }

    private TableLayoutPanel BuildLayout()
    {
        var taps = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        taps.Controls.AddRange([_everyPress, _doubleTap, _windowLabel, _window, _windowUnit]);
        foreach (Control control in taps.Controls)
            control.Margin = new Padding(0, 6, 8, 0);

        var presets = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        foreach (var (label, trigger) in Presets)
        {
            var preset = new Button { Text = label, AutoSize = true, Margin = new Padding(0, 0, 6, 0) };

            // Captured in the closure rather than read back from a Tag: the preset is immutable and there is no
            // reason for the click to go looking for it again.
            preset.Click += (_, _) => Apply(trigger);
            presets.Controls.Add(preset);
        }

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange([_cancel, _ok]);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = "Press the mouse button or key combination you want to use. A left or right click counts only "
                + "with Ctrl, Alt or Shift held, so SmartZoom can never take over a normal click.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 56,
            Margin = new Padding(0, 0, 0, 8),
        });
        layout.Controls.Add(_captured);
        layout.Controls.Add(_presetsLabel);
        layout.Controls.Add(presets);
        layout.Controls.Add(taps);
        layout.Controls.Add(_swallow);
        layout.Controls.Add(buttons);
        return layout;
    }

    /// <summary>Fills the dialog in from a preset, exactly as if the user had pressed it.</summary>
    private void Apply(TriggerSettings preset)
    {
        Restore(preset, (uint)_window.Value);
        UpdateWindowEnabled();
    }

    /// <summary>
    /// The button (with its modifiers) and the combination a trigger stands for, exactly one of them set:
    /// whichever the trigger does not name is cleared, so restoring a trigger over another leaves nothing of
    /// the first behind. Modifiers that cannot be read count as none.
    /// </summary>
    /// <param name="trigger">The trigger to show.</param>
    internal static (MouseButton? Button, KeyModifiers Modifiers, KeyCombo? Combo) Recorded(TriggerSettings trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        var modifiers = trigger.Mouse is not null && KeyCombo.TryParseModifiers(trigger.Modifiers, out var held) ? held : KeyModifiers.None;
        return (trigger.Mouse, modifiers, KeyCombo.TryParse(trigger.Keys, out var combo) ? combo : null);
    }

    /// <summary>The trigger button a WinForms button stands for, or <see cref="MouseButton.None"/>.</summary>
    /// <param name="button">The button WinForms reported.</param>
    internal static MouseButton ButtonOf(MouseButtons button) => button switch
    {
        MouseButtons.Left => MouseButton.Left,
        MouseButtons.Right => MouseButton.Right,
        MouseButtons.Middle => MouseButton.Middle,
        MouseButtons.XButton1 => MouseButton.XButton1,
        MouseButtons.XButton2 => MouseButton.XButton2,
        _ => MouseButton.None,
    };

    /// <summary>
    /// The modifiers in a WinForms key state. Only Ctrl, Alt and Shift: WinForms does not report the Windows key
    /// there, so a click with it is not something this dialog can record.
    /// </summary>
    /// <param name="keys">The key state, usually <see cref="Control.ModifierKeys"/>.</param>
    internal static KeyModifiers ModifiersOf(Keys keys)
    {
        var modifiers = KeyModifiers.None;
        if ((keys & Keys.Control) != 0)
            modifiers |= KeyModifiers.Control;
        if ((keys & Keys.Alt) != 0)
            modifiers |= KeyModifiers.Alt;
        if ((keys & Keys.Shift) != 0)
            modifiers |= KeyModifiers.Shift;
        return modifiers;
    }

    private void Restore(TriggerSettings existing, uint systemDoubleClickMs)
    {
        (_button, _modifiers, _combo) = Recorded(existing);

        _doubleTap.Checked = existing.TapCount == 2;
        _everyPress.Checked = existing.TapCount != 2;
        _window.Value = existing.DoubleTapWindowMs ?? systemDoubleClickMs;
        _swallow.Checked = existing.SwallowClicks;
        Show(Describe());
    }

    private void OnCaptureMouseDown(object? sender, MouseEventArgs e)
    {
        var button = ButtonOf(e.Button);
        if (button == MouseButton.None)
        {
            Show("That button can't be a trigger. Try the wheel click or a side button.");
            return;
        }

        // Left and right count only with a modifier held: a global hook that can delay or swallow bare primary
        // clicks would make the machine unusable when SmartZoom misbehaves. MouseButtonTrigger refuses them too.
        var modifiers = ModifiersOf(Control.ModifierKeys);
        if (button is MouseButton.Left or MouseButton.Right && modifiers == KeyModifiers.None)
        {
            Show("A left or right click needs a modifier key: hold Ctrl, Alt or Shift while you click.");
            return;
        }

        _heldModifier = null;
        _button = button;
        _modifiers = modifiers;
        _combo = null;

        // Accepted, with the same caution the settings check gives: Ctrl+click and Shift+click are spoken for elsewhere.
        var caution = MouseButtonTrigger.CautionFor(button, modifiers);
        Show(caution is null ? Describe() : $"{Describe()}\n{caution}");
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

        if (VirtualKeys.TryGetModifier(key, out _))
        {
            // Not recorded yet: a click while it is held makes a modifier + click trigger; releasing it makes
            // the bare modifier the trigger. Either way the user is not asked to race the keyboard.
            _heldModifier = combo;
            Show($"Holding {combo}: click a mouse button to combine them, or let go to use {combo} on its own.");
            return;
        }

        _heldModifier = null;
        _combo = combo;
        _button = null;
        _modifiers = KeyModifiers.None;
        Show(Describe());
    }

    private void OnCaptureKeyUp(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (_heldModifier is not { } held || !VirtualKeys.TryGetModifier(VirtualKeys.Fold((ushort)e.KeyValue), out _))
            return;

        // The modifier came back up with nothing clicked while it was down: it is the trigger by itself.
        _heldModifier = null;
        _combo = held;
        _button = null;
        _modifiers = KeyModifiers.None;
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

    private string Describe() =>
        _button is { } button ? MouseButtonTrigger.Describe(button, _modifiers) : _combo?.ToString() ?? string.Empty;

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
