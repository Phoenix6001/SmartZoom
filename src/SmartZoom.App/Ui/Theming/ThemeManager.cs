using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

using Microsoft.Extensions.Logging;

using SmartZoom.Core.Settings;
using SmartZoom.Interop.Windows;

using WpfApplication = System.Windows.Application;

namespace SmartZoom.App.Ui.Theming;

/// <summary>
/// Decides which palette the settings window wears and swaps it while the window is open.
/// </summary>
/// <remarks>
/// <para>
/// Exactly one palette dictionary is merged into the application's resources, at a known slot. Swapping that
/// one entry is the whole mechanism: every brush in the UI is reached through <c>DynamicResource</c>, so the
/// window repaints in place and nothing has to be rebuilt or reopened.
/// </para>
/// <para>
/// <see cref="AppearanceMode.System"/> is not a third palette but a subscription: the value Windows writes is
/// read once at startup and again on every <c>WM_SETTINGCHANGE</c> that names <c>ImmersiveColorSet</c>, which
/// is the only notice the shell gives. The frame outside our own drawing is told separately, through the
/// desktop window manager, so a maximised window's border agrees with what is inside it.
/// </para>
/// </remarks>
internal sealed partial class ThemeManager(ILogger<ThemeManager> logger)
{
    /// <summary>The broadcast Windows sends when something in the control panel changed.</summary>
    private const int WmSettingChange = 0x001A;

    /// <summary>The one value of <c>lParam</c> that means the colours changed.</summary>
    private const string ColorSetChanged = "ImmersiveColorSet";

    private static readonly Uri LightPalette = new("/SmartZoom;component/Ui/Themes/Light.xaml", UriKind.Relative);
    private static readonly Uri DarkPalette = new("/SmartZoom;component/Ui/Themes/Dark.xaml", UriKind.Relative);
    private static readonly Uri Controls = new("/SmartZoom;component/Ui/Themes/Controls.xaml", UriKind.Relative);

    private readonly List<Window> _windows = [];
    private WpfApplication? _application;
    private ResourceDictionary? _palette;

    /// <summary>Raised after the palette has changed, on the UI thread.</summary>
    public event EventHandler? Changed;

    /// <summary>What the user asked for: a fixed palette, or whatever Windows is using.</summary>
    public AppearanceMode Mode { get; private set; } = AppearanceMode.System;

    /// <summary>Whether the dark palette is the one on screen right now.</summary>
    public bool IsDark { get; private set; }

    /// <summary>
    /// Puts the shared styles and the first palette into the application's resources. Does nothing the
    /// second time, so a caller that cannot easily tell whether the window has been opened before may call it
    /// whenever it is about to need one.
    /// </summary>
    /// <param name="application">The WPF application whose resources the window resolves against.</param>
    /// <param name="mode">The appearance from the settings file.</param>
    public void Install(WpfApplication application, AppearanceMode mode)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (_application is not null)
            return;

        _application = application;
        Mode = mode;

        var wanted = Resolve(mode);
        _palette = Load(wanted);
        application.Resources.MergedDictionaries.Add(_palette);
        application.Resources.MergedDictionaries.Add(Load(Controls));
        IsDark = wanted == DarkPalette;
    }

    /// <summary>
    /// Changes the appearance. Callers persist the choice through <c>SettingsApplier</c>; this only paints.
    /// </summary>
    /// <param name="mode">The appearance to wear.</param>
    public void Use(AppearanceMode mode)
    {
        Mode = mode;
        Apply();
    }

    /// <summary>The appearance after <see cref="Mode"/> cycles once: light, then dark, then back to Windows.</summary>
    public AppearanceMode Next() => Mode switch
    {
        AppearanceMode.Light => AppearanceMode.Dark,
        AppearanceMode.Dark => AppearanceMode.System,
        _ => AppearanceMode.Light,
    };

    /// <summary>
    /// Keeps a window's frame in step with the palette, and listens on it for the shell's colour broadcast.
    /// </summary>
    /// <param name="window">The window; it must already have a handle.</param>
    public void Follow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (_windows.Contains(window))
            return;

        _windows.Add(window);

        if (PresentationSource.FromVisual(window) is HwndSource source)
            source.AddHook(OnWindowMessage);

        WindowTheme.SetDarkFrame(new WindowInteropHelper(window).Handle, IsDark);
    }

    /// <summary>Re-resolves the palette and swaps it if it is not the one already merged.</summary>
    private void Apply()
    {
        if (_application is null || _palette is null)
            return;

        var wanted = Resolve(Mode);
        var dark = wanted == DarkPalette;
        if (dark == IsDark)
        {
            // Still tell the frames: a window that opened before the first swap may not have been told yet.
            Frames();
            return;
        }

        var replacement = Load(wanted);
        var slot = _application.Resources.MergedDictionaries.IndexOf(_palette);
        if (slot < 0)
            _application.Resources.MergedDictionaries.Insert(0, replacement);
        else
            _application.Resources.MergedDictionaries[slot] = replacement;

        _palette = replacement;
        IsDark = dark;
        LogSwapped(dark ? "dark" : "light", Mode);

        Frames();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Frames()
    {
        foreach (var window in _windows)
            WindowTheme.SetDarkFrame(new WindowInteropHelper(window).Handle, IsDark);
    }

    private static Uri Resolve(AppearanceMode mode) => mode switch
    {
        AppearanceMode.Light => LightPalette,
        AppearanceMode.Dark => DarkPalette,
        _ => WindowsAppearance.IsDark ? DarkPalette : LightPalette,
    };

    private static ResourceDictionary Load(Uri source) => new() { Source = source };

    private IntPtr OnWindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmSettingChange || Mode != AppearanceMode.System || lParam == IntPtr.Zero)
            return IntPtr.Zero;

        // The broadcast carries the name of what changed, not its value; anything else is somebody else's.
        if (string.Equals(Marshal.PtrToStringUni(lParam), ColorSetChanged, StringComparison.Ordinal))
            Apply();

        return IntPtr.Zero;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Settings window switched to the {Palette} palette (appearance: {Mode}).")]
    private partial void LogSwapped(string palette, AppearanceMode mode);
}
