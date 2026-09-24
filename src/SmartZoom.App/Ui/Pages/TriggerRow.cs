namespace SmartZoom.App.Ui.Pages;

/// <summary>One configured trigger, as the Triggers page shows it.</summary>
/// <param name="Index">
/// Where it sits in <c>Triggers</c>. The list is the only thing that holds the order, and Edit and Remove act
/// on a position rather than on a value, because two identical triggers are allowed to exist.
/// </param>
/// <param name="DisplayName">The trigger as the hook and the log spell it, e.g. "XButton2 x1".</param>
/// <param name="Behaviour">
/// What it does when pressed, in one muted line: how many presses it takes, and whether the application
/// underneath ever sees them.
/// </param>
internal sealed record TriggerRow(int Index, string DisplayName, string Behaviour);
