using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SmartZoom.App.Ui.Controls;

/// <summary>Makes a screen reader read a line of text out when it appears or changes.</summary>
/// <remarks>
/// <para>
/// For text that answers something the user just did: what the recorder caught, a refused change, "Copied".
/// Someone who cannot see the window would otherwise press, hear nothing, and not know whether it worked.
/// </para>
/// <para>
/// WPF's <see cref="AutomationProperties.LiveSettingProperty"/> only marks the element; the reader is told
/// nothing until the element raises <see cref="AutomationEvents.LiveRegionChanged"/>, which a bound
/// <see cref="TextBlock.Text"/> never does by itself. So this watches the text while the element is loaded and
/// raises the event when it changes, and once when it is loaded with something to say (a problem line is a new
/// element, not a changed one). The event is raised after layout, because text often changes in the same
/// breath as the visibility that shows it.
/// </para>
/// </remarks>
internal static class LiveRegion
{
    /// <summary>Whether the text is read out when it appears or changes.</summary>
    public static readonly DependencyProperty AnnounceProperty = DependencyProperty.RegisterAttached(
        "Announce",
        typeof(bool),
        typeof(LiveRegion),
        new PropertyMetadata(false, OnAnnounceChanged));

    private static readonly DependencyPropertyDescriptor TextDescriptor =
        DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));

    /// <summary>Gets whether the text is read out when it appears or changes.</summary>
    /// <param name="element">The text block.</param>
    /// <returns>Whether it is announced.</returns>
    public static bool GetAnnounce(TextBlock element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(AnnounceProperty);
    }

    /// <summary>Sets whether the text is read out when it appears or changes.</summary>
    /// <param name="element">The text block.</param>
    /// <param name="value">Whether to announce it.</param>
    public static void SetAnnounce(TextBlock element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(AnnounceProperty, value);
    }

    private static void OnAnnounceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock text)
            return;

        text.Loaded -= OnLoaded;
        text.Unloaded -= OnUnloaded;
        if (e.NewValue is not true)
        {
            Stop(text);
            return;
        }

        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        text.Loaded += OnLoaded;
        text.Unloaded += OnUnloaded;
        if (text.IsLoaded)
            Start(text);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var text = (TextBlock)sender;
        Start(text);
        Announce(text);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e) => Stop((TextBlock)sender);

    // The descriptor holds the element strongly while subscribed, so the subscription lasts only while loaded.
    private static void Start(TextBlock text)
    {
        TextDescriptor.RemoveValueChanged(text, OnTextChanged);
        TextDescriptor.AddValueChanged(text, OnTextChanged);
    }

    private static void Stop(TextBlock text) => TextDescriptor.RemoveValueChanged(text, OnTextChanged);

    private static void OnTextChanged(object? sender, EventArgs e)
    {
        if (sender is TextBlock text)
            Announce(text);
    }

    private static void Announce(TextBlock text)
    {
        if (!AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            return;

        text.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (string.IsNullOrWhiteSpace(text.Text) || !text.IsVisible)
                return;

            var peer = UIElementAutomationPeer.FromElement(text) ?? UIElementAutomationPeer.CreatePeerForElement(text);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        });
    }
}
