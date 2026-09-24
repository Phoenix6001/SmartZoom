using SmartZoom.Core.Routing;

namespace SmartZoom.App.Ui.Pages;

/// <summary>
/// One entry of the "handled by" list: a zoom strategy this build actually has, or the choice to leave an
/// application alone.
/// </summary>
/// <remarks>
/// Built from <see cref="AdapterDescriptor"/> rather than from a list kept here, so a strategy added in a
/// later build offers itself — with its own name and its own sentence — without this page being edited.
/// </remarks>
/// <param name="Id">The id written into <c>Routing.Apps</c>; <see cref="AdapterId.None"/> for "Not handled".</param>
/// <param name="DisplayName">What the combo box shows.</param>
/// <param name="Description">One sentence on what it does, shown under the combo and as its tooltip.</param>
internal sealed record StrategyChoice(AdapterId Id, string DisplayName, string Description)
{
    /// <summary>The choice that switches SmartZoom off for an application.</summary>
    public static StrategyChoice NotHandled { get; } = new(
        AdapterId.None,
        "Not handled",
        "SmartZoom ignores this application; a trigger there does nothing and its own zoom is left alone.");

    /// <summary>The strategy an adapter offers.</summary>
    /// <param name="adapter">What the adapter says about itself.</param>
    public static StrategyChoice From(AdapterDescriptor adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        return new StrategyChoice(adapter.Id, adapter.DisplayName, adapter.Description);
    }
}
