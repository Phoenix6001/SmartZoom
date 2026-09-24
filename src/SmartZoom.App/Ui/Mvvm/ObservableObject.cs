using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartZoom.App.Ui.Mvvm;

/// <summary>Base for the view models behind the settings window: raises a change notification per property.</summary>
/// <remarks>
/// Hand-written rather than taken from an MVVM framework. The window needs change notification and a command
/// type and nothing else, and a dependency the whole application would carry for two small classes is a poor
/// trade in a tray app that is otherwise framework-free.
/// </remarks>
internal abstract class ObservableObject : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Announces that a property has a new value.</summary>
    /// <param name="name">The property; supplied by the compiler at the call site.</param>
    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Stores a value and announces it, unless it is the one already there.</summary>
    /// <typeparam name="T">The property's type.</typeparam>
    /// <param name="field">The backing field.</param>
    /// <param name="value">The new value.</param>
    /// <param name="name">The property; supplied by the compiler at the call site.</param>
    /// <returns>True when the value changed.</returns>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        Raise(name);
        return true;
    }
}
