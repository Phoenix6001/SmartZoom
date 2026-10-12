namespace SmartZoom.Core.Zoom.Content;

/// <summary>What the platform side does next, as <see cref="HitTestPolicy"/> decided from the last answer.</summary>
public enum HitTestAction
{
    /// <summary>Finished: <see cref="HitTestStep.Hit"/> is the result, or null when there is nothing to zoom.</summary>
    Done,

    /// <summary>Ask the tree again at once, at <see cref="HitTestPolicy.Query"/>.</summary>
    AskAgain,

    /// <summary>The tree is not ready: wait <see cref="HitTestPolicy.RetryDelay"/>, then ask again.</summary>
    Wait,

    /// <summary>The tree refused: forget its root, wait <see cref="HitTestPolicy.RetryDelay"/>, take it again and ask.</summary>
    Reacquire,
}
