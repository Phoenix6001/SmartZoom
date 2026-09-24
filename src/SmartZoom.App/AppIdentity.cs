namespace SmartZoom.App;

/// <summary>The names by which one SmartZoom process finds another.</summary>
internal static class AppIdentity
{
    /// <summary>
    /// Names the single-instance mutex and prefixes the events a second copy signals it through. "Local\"
    /// scopes both to the current logon session, so other signed-in users can still run their own copy.
    /// </summary>
    public const string InstanceName = @"Local\SmartZoom.App-9C7B1E52-3F0A-4C1F-8B7D-2E6A5D4C3B21";
}
