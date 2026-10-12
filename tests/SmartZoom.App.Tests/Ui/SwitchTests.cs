using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

using SmartZoom.App.Ui.Controls;
using SmartZoom.App.Ui.Mvvm;

namespace SmartZoom.App.Tests.Ui;

/// <summary>
/// The settings window's on/off switch. It shows a setting and asks its command to change it; a screen reader
/// must be told what it shows, and turning it from a screen reader must do what a click does.
/// </summary>
public sealed class SwitchTests
{
    [Fact]
    public void A_screen_reader_is_told_whether_it_is_on()
    {
        OnSta(() =>
        {
            var on = new Switch { IsChecked = true };
            var off = new Switch { IsChecked = false };

            Assert.Equal(ToggleState.On, Toggle(on).ToggleState);
            Assert.Equal(ToggleState.Off, Toggle(off).ToggleState);
        });
    }

    [Fact]
    public void Turning_it_from_a_screen_reader_runs_the_command()
    {
        OnSta(() =>
        {
            var runs = 0;
            var control = new Switch { Command = new RelayCommand(() => runs++) };

            Toggle(control).Toggle();

            Assert.Equal(1, runs);
        });
    }

    [Fact]
    public void It_does_not_flip_itself_so_a_refused_change_is_not_shown_as_made()
    {
        OnSta(() =>
        {
            // The command declines to change anything, as the settings do when a change is refused.
            var control = new Switch { IsChecked = false, Command = new RelayCommand(() => { }) };

            Toggle(control).Toggle();
            control.Flip();

            Assert.False(control.IsChecked);
        });
    }

    [Fact]
    public void A_switch_that_cannot_be_used_refuses_a_screen_readers_toggle()
    {
        OnSta(() =>
        {
            var runs = 0;
            var control = new Switch { IsEnabled = false, Command = new RelayCommand(() => runs++) };

            Assert.Throws<ElementNotEnabledException>(() => Toggle(control).Toggle());
            Assert.Equal(0, runs);
        });
    }

    [Fact]
    public void Announced_text_is_marked_as_a_live_region()
    {
        OnSta(() =>
        {
            var text = new TextBlock();

            LiveRegion.SetAnnounce(text, true);

            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(text));
        });
    }

    private static IToggleProvider Toggle(Switch control) =>
        (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(control).GetPattern(PatternInterface.Toggle);

    /// <summary>Runs a test body on its own STA thread, which every WPF element requires.</summary>
    private static void OnSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The STA thread did not finish.");
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
