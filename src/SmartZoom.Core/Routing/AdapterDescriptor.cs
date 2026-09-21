namespace SmartZoom.Core.Routing;

/// <summary>
/// What an adapter says about itself: its id, the applications it handles out of the box, and enough
/// prose for a settings UI to offer it. This is the whole registration contract — an adapter that
/// returns a descriptor and is registered in the composition root is routable, with no list to edit.
/// </summary>
/// <param name="Id">The name used in <c>Routing.Apps</c>.</param>
/// <param name="DefaultProcesses">
/// Process image names, without ".exe". They become routes unless the user's settings say otherwise.
/// Two adapters claiming the same process is an error at startup, not a silent last-one-wins.
/// </param>
/// <param name="DisplayName">Short label for a settings UI, e.g. "Browsers".</param>
/// <param name="Description">One sentence on what it does and what it needs, for the same UI and for the docs.</param>
public sealed record AdapterDescriptor(
    AdapterId Id,
    IReadOnlyList<string> DefaultProcesses,
    string DisplayName,
    string Description)
{
    /// <summary>Creates a descriptor from a plain id string.</summary>
    /// <param name="id">The name used in <c>Routing.Apps</c>.</param>
    /// <param name="defaultProcesses">Process image names handled out of the box.</param>
    /// <param name="displayName">Short label for a settings UI.</param>
    /// <param name="description">One sentence on what it does.</param>
    public AdapterDescriptor(string id, IReadOnlyList<string> defaultProcesses, string displayName, string description)
        : this(new AdapterId(id), defaultProcesses, displayName, description)
    {
    }
}
