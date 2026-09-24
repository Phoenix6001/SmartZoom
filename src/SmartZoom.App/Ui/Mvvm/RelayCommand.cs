using System.Windows.Input;

namespace SmartZoom.App.Ui.Mvvm;

/// <summary>An <see cref="ICommand"/> over a delegate, so a button in XAML needs no code-behind handler.</summary>
/// <param name="execute">What the command does. The parameter is the button's CommandParameter, or null.</param>
/// <param name="canExecute">Whether it may run right now; null means always.</param>
internal sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    /// <summary>Creates a command that takes no parameter.</summary>
    /// <param name="execute">What the command does.</param>
    public RelayCommand(Action execute)
        : this(_ => execute())
    {
    }

    /// <inheritdoc />
    /// <remarks>Routed through WPF's requery so the UI re-asks whenever it re-evaluates input state.</remarks>
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => canExecute is null || canExecute(parameter);

    /// <inheritdoc />
    public void Execute(object? parameter) => execute(parameter);
}
