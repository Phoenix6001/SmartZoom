namespace SmartZoom.Core.Zoom.Content;

/// <summary>One decision of <see cref="HitTestPolicy"/>.</summary>
/// <param name="Action">What to do next.</param>
/// <param name="Hit">The result, when <paramref name="Action"/> is <see cref="HitTestAction.Done"/>.</param>
/// <param name="ForgetRoot">
/// Whether to drop the cached accessible root, so the next press takes a new one (and, in Chromium, a new
/// handshake) rather than asking a tree that has just failed to answer.
/// </param>
/// <param name="Nudge">With <see cref="HitTestAction.Wait"/>: whether to repeat Chromium's wake-up on the root first.</param>
public readonly record struct HitTestStep(HitTestAction Action, ContentHit? Hit = null, bool ForgetRoot = false, bool Nudge = false);
