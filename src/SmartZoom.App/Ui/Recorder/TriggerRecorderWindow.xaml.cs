using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using SmartZoom.App.Settings;
using SmartZoom.Core.Input;
using SmartZoom.Core.Settings;

// WinForms and WPF are both in scope in this project, and they disagree about several of these names.
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Control = System.Windows.Forms.Control;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButton = SmartZoom.Core.Input.MouseButton;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;
using WpfMouseButton = System.Windows.Input.MouseButton;

namespace SmartZoom.App.Ui.Recorder;

/// <summary>
/// Asks the user to perform the gesture they want, rather than to name it. The one thing everybody has to
/// configure is which button starts a zoom, and "press it" is the only instruction that needs no knowledge of
/// what Windows calls the button behind the scroll wheel.
/// </summary>
/// <remarks>
/// <para>
/// SmartZoom's own trigger is switched off for as long as this is open. Otherwise pressing the button you are
/// trying to record would zoom the window behind it.
/// </para>
/// <para>
/// The rules about what may be recorded are not restated here: <see cref="TriggerRecording"/> holds them
/// as pure statics with tests of their own, and this window calls those. What is here is the surface — the
/// capture area, the presets, and the tap and swallow settings — plus the two things that can only be done
/// where the input arrives: ignoring auto-repeat, and telling a click on the dialog's own controls apart from
/// a click meant as the trigger.
/// </para>
/// </remarks>
internal sealed partial class TriggerRecorderWindow : Window
{
    /// <summary>
    /// The handful of triggers most people settle on, as one click each. Pressing your own is still the point
    /// of this window; these are here because a side button has no name anybody recognises, and on an
    /// unfamiliar mouse "the forward side button" is easier to pick from a list than to find and press.
    /// </summary>
    /// <remarks>
    /// Each preset carries its own tap and swallow policy, because they are not interchangeable. A dedicated
    /// side button is best hidden from the application underneath, and with a single press there is no delay
    /// in doing so. The wheel button already means "open in a new tab" and "autoscroll", so it needs the
    /// double-tap and must NOT be hidden — hiding it would hold every middle click back by the whole
    /// double-tap window.
    /// </remarks>
    private static readonly TriggerPreset[] Choices =
    [
        new("Forward side button", new TriggerSettings { Mouse = MouseButton.XButton2, TapCount = 1, SwallowClicks = true }),
        new("Back side button", new TriggerSettings { Mouse = MouseButton.XButton1, TapCount = 1, SwallowClicks = true }),
        new("Double-click the wheel", new TriggerSettings { Mouse = MouseButton.Middle, TapCount = 2, SwallowClicks = false }),
        new("Ctrl+Alt+Z", new TriggerSettings { Keys = "Ctrl+Alt+Z", TapCount = 1, SwallowClicks = true }),
    ];

    private readonly ITriggerSource? _triggers;
    private readonly bool _triggersWereEnabled;
    private readonly uint _systemDoubleClickMs;

    private MouseButton? _button;
    private KeyModifiers _modifiers;
    private KeyCombo? _combo;

    /// <summary>
    /// A modifier that is down but not yet recorded. A held modifier can be the start of a modifier + click
    /// trigger or a trigger on its own ("double-tap Ctrl"); which one is known only when the next thing
    /// happens: a click while it is held, or its release.
    /// </summary>
    private KeyCombo? _heldModifier;

    /// <summary>Opens the recorder, optionally on an existing trigger.</summary>
    /// <param name="triggers">
    /// Silenced while the window is open. Null when nothing is running to silence — the installer shows this
    /// before SmartZoom has ever started.
    /// </param>
    /// <param name="systemDoubleClickMs">The default double-tap window, when the trigger does not set one.</param>
    /// <param name="existing">The trigger being edited, or null to record a new one.</param>
    public TriggerRecorderWindow(ITriggerSource? triggers, uint systemDoubleClickMs, TriggerSettings? existing)
    {
        InitializeComponent();

        _triggers = triggers;
        _systemDoubleClickMs = systemDoubleClickMs;

        // Silenced rather than stopped: stopping the hook would drop the user's other triggers too, and this
        // window is exactly where they are most likely to press the one that is already configured.
        _triggersWereEnabled = triggers?.Enabled ?? false;
        if (triggers is not null)
            triggers.Enabled = false;

        Title = existing is null ? "New trigger" : "Edit trigger";
        TitleText.Text = Title;
        Presets.ItemsSource = Choices;
        WindowMs.Text = systemDoubleClickMs.ToString(CultureInfo.CurrentCulture);
        DoubleTap.Checked += (_, _) => UpdateWindowEnabled();
        DoubleTap.Unchecked += (_, _) => UpdateWindowEnabled();

        if (existing is not null)
            Restore(existing);

        UpdateWindowEnabled();
    }

    /// <summary>What was recorded. Only meaningful after the window closed with a true dialog result.</summary>
    public TriggerSettings Result => TriggerRecording.Compose(
        _button,
        _modifiers,
        _combo,
        DoubleTap.IsChecked == true,
        TapWindowMs(),
        Swallow.IsChecked == true);

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (_triggers is not null)
            _triggers.Enabled = _triggersWereEnabled;

        base.OnClosed(e);
    }

    /// <summary>
    /// Records a click anywhere on the window, except on something that is meant to be clicked. Handled as a
    /// preview so the click reaches the recorder before any control can consume it.
    /// </summary>
    /// <param name="e">The click.</param>
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (IsControl(e.OriginalSource as DependencyObject))
        {
            base.OnPreviewMouseDown(e);
            return;
        }

        Capture(e.ChangedButton);
        e.Handled = true;
        base.OnPreviewMouseDown(e);
    }

    /// <inheritdoc />
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // A held modifier repeats every few tens of milliseconds, and each repeat would otherwise look like a
        // new press and turn a click that was just recorded back into "holding Ctrl". This is WPF's name for
        // the same lParam bit the message loop would have to read.
        if (e.IsRepeat)
        {
            e.Handled = true;
            return;
        }

        // Nothing typed here should reach a control; the window's own buttons are reached with the mouse.
        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            Cancel();
            return;
        }

        var key = VirtualKeys.Fold((ushort)KeyInterop.VirtualKeyFromKey(RealKey(e)));
        var modifiers = HeldModifiers();

        // A bare modifier is a legitimate trigger ("double-tap Ctrl"), so it is not filtered out; it just
        // must not appear both as the key and as one of the modifiers it is held with.
        if (VirtualKeys.TryGetModifier(key, out var self))
            modifiers &= ~self;

        var combo = new KeyCombo(modifiers, key);
        if (!KeyCombo.TryParse(combo.ToString(), out _))
        {
            // Media keys, IME keys and the like have no name the settings file could round-trip.
            Report("That key can't be written down, so it can't be a trigger.", caution: null);
            return;
        }

        if (VirtualKeys.TryGetModifier(key, out _))
        {
            // Not recorded yet: a click while it is held makes a modifier + click trigger; releasing it makes
            // the bare modifier the trigger. Either way the user is not asked to race the keyboard.
            _heldModifier = combo;
            Report($"Holding {combo}: click a mouse button to combine them, or let go to use {combo} on its own.", caution: null);
            return;
        }

        _heldModifier = null;
        _combo = combo;
        _button = null;
        _modifiers = KeyModifiers.None;
        Report(Describe(), caution: null);
    }

    /// <inheritdoc />
    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        e.Handled = true;

        var key = VirtualKeys.Fold((ushort)KeyInterop.VirtualKeyFromKey(RealKey(e)));
        if (_heldModifier is not { } held || !VirtualKeys.TryGetModifier(key, out _))
            return;

        // The modifier came back up with nothing clicked while it was down: it is the trigger by itself.
        _heldModifier = null;
        _combo = held;
        _button = null;
        _modifiers = KeyModifiers.None;
        Report(Describe(), caution: null);
    }

    /// <summary>Alt arrives as <see cref="Key.System"/>, with the key it was pressed with in SystemKey.</summary>
    private static Key RealKey(KeyEventArgs e) => e.Key == Key.System ? e.SystemKey : e.Key;

    /// <summary>Whether a click landed on something that is meant to be clicked rather than recorded.</summary>
    /// <remarks>
    /// Walked up the visual tree rather than tested on the source itself, because the thing under the pointer
    /// is a button's inner border or its text, not the button.
    /// </remarks>
    private static bool IsControl(DependencyObject? source)
    {
        for (var node = source; node is Visual; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ButtonBase or TextBoxBase)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The modifiers held right now. Ctrl, Alt and Shift come from the same helper the WinForms recorder uses,
    /// which has tests; the Windows key is added from WPF's own state, which WinForms never reported.
    /// </summary>
    private static KeyModifiers HeldModifiers()
    {
        var modifiers = TriggerRecording.ModifiersOf(Control.ModifierKeys);
        if ((Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Windows) != 0)
            modifiers |= KeyModifiers.Win;

        return modifiers;
    }

    /// <summary>The trigger button a WPF mouse button stands for, through the tested WinForms mapping.</summary>
    private static MouseButton ButtonOf(WpfMouseButton button) => TriggerRecording.ButtonOf(button switch
    {
        WpfMouseButton.Left => MouseButtons.Left,
        WpfMouseButton.Right => MouseButtons.Right,
        WpfMouseButton.Middle => MouseButtons.Middle,
        WpfMouseButton.XButton1 => MouseButtons.XButton1,
        WpfMouseButton.XButton2 => MouseButtons.XButton2,
        _ => MouseButtons.None,
    });

    private void Capture(WpfMouseButton pressed)
    {
        var button = ButtonOf(pressed);
        if (button == MouseButton.None)
        {
            Report("That button can't be a trigger. Try the wheel click or a side button.", caution: null);
            return;
        }

        // Left and right count only with a modifier held: a global hook that can delay or swallow bare primary
        // clicks would make the machine unusable when SmartZoom misbehaves. MouseButtonTrigger refuses them too.
        var modifiers = HeldModifiers();
        if (button is MouseButton.Left or MouseButton.Right && modifiers == KeyModifiers.None)
        {
            Report("A left or right click needs a modifier key: hold Ctrl, Alt or Shift while you click.", caution: null);
            return;
        }

        _heldModifier = null;
        _button = button;
        _modifiers = modifiers;
        _combo = null;

        // Accepted, with the same caution the settings check gives: Ctrl+click and Shift+click are spoken for
        // elsewhere. It goes on its own line, in the warning colour, rather than beside the trigger itself.
        Report(Describe(), MouseButtonTrigger.CautionFor(button, modifiers));
    }

    private void Restore(TriggerSettings existing)
    {
        (_button, _modifiers, _combo) = TriggerRecording.Recorded(existing);

        DoubleTap.IsChecked = existing.TapCount == 2;
        EveryPress.IsChecked = existing.TapCount != 2;
        WindowMs.Text = (existing.DoubleTapWindowMs ?? _systemDoubleClickMs).ToString(CultureInfo.CurrentCulture);
        Swallow.IsChecked = existing.SwallowClicks;
        Report(Describe(), _button is { } button ? MouseButtonTrigger.CautionFor(button, _modifiers) : null);
    }

    private string Describe() =>
        _button is { } button ? MouseButtonTrigger.Describe(button, _modifiers) : _combo?.ToString() ?? string.Empty;

    private void Report(string text, string? caution)
    {
        Captured.Text = text;
        Caution.Text = caution ?? string.Empty;
        Caution.Visibility = caution is null ? Visibility.Collapsed : Visibility.Visible;
        Ok.IsEnabled = _button is not null || _combo is not null;
    }

    /// <summary>The double-tap window as typed, falling back to the system's when it is not a number.</summary>
    private uint TapWindowMs() =>
        uint.TryParse(WindowMs.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var ms) && ms is > 0 and <= 5000
            ? ms
            : _systemDoubleClickMs;

    private void UpdateWindowEnabled()
    {
        var doubleTap = DoubleTap.IsChecked == true;
        WindowLabel.IsEnabled = doubleTap;
        WindowMs.IsEnabled = doubleTap;
        WindowUnit.IsEnabled = doubleTap;
    }

    private void Cancel() => DialogResult = false;

    private void OnCancel(object sender, RoutedEventArgs e) => Cancel();

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    /// <summary>Fills the window in from a preset, exactly as if the user had pressed it.</summary>
    private void OnPreset(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TriggerSettings preset })
        {
            Restore(preset);
            UpdateWindowEnabled();
        }
    }
}
