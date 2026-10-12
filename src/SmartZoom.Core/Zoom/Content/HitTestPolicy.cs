using Microsoft.Extensions.Logging;

using SmartZoom.Core.Input;

namespace SmartZoom.Core.Zoom.Content;

/// <summary>Decides, one answer at a time, what a browser's accessibility tree said about the point under the cursor.</summary>
/// <remarks>
/// <para>
/// One instance per press. The platform side asks the tree at <see cref="Query"/>, hands the raw answer to
/// <see cref="Next"/>, and does what the returned <see cref="HitTestStep"/> says, until it says
/// <see cref="HitTestAction.Done"/>. Every rule about what an answer means lives here, where each answer a
/// browser has been seen to give can be replayed in a test:
/// </para>
/// <list type="bullet">
/// <item>A tree that throws is being built (a cold browser, a page still loading): take its root again and retry.</item>
/// <item>
/// A path that stops at the document or at a page-wide placeholder is a tree the renderer has not finished
/// serializing: wait a frame and ask again, nudging Chromium's wake-up meanwhile.
/// </item>
/// <item>A document wider than its window is a tree left at the scale of an earlier pinch (<see cref="StalePageZoom"/>):
/// ask again where the tree believes the cursor is, and translate its answer back.</item>
/// <item>A leaf whose rectangle does not hold the cursor is a tree that has not caught up with a scroll: wait.</item>
/// <item>A path that never reaches a document, even an empty one, takes the render window as its page on the last attempt.</item>
/// <item>Only the outermost document is the page; a frame's document is a box on it (<see cref="ContentPath.ToPage"/>).</item>
/// </list>
/// <para>
/// Gecko's tree is complete from the first call and does not bake a pinch into its rectangles, so it is never
/// nudged and never read as a stale zoom.
/// </para>
/// </remarks>
/// <param name="cursor">Where the cursor is, in physical screen pixels.</param>
/// <param name="gecko">Whether the browser is Gecko (Firefox) rather than Chromium.</param>
/// <param name="process">The browser's process name, for the log.</param>
/// <param name="logger">Where to log; the platform side passes its own, so the lines keep its category.</param>
public sealed partial class HitTestPolicy(ScreenPoint cursor, bool gecko, string? process, ILogger logger)
{
    /// <summary>How many times the tree is asked before the press settles for what it has.</summary>
    public const int MaxAttempts = 12;

    /// <summary>How long to wait between attempts that wait. Twelve of them is about 600 ms.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

    private readonly ScreenPoint _cursor = cursor;

    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Where to ask the tree next: the cursor, unless the tree reports a stale page zoom.</summary>
    public ScreenPoint Query { get; private set; } = cursor;

    /// <summary>How many answers have been judged so far.</summary>
    public int Attempts { get; private set; }

    /// <summary>Judges one answer from the tree.</summary>
    /// <param name="answer">What the tree said when asked at <see cref="Query"/>.</param>
    /// <returns>What to do next.</returns>
    public HitTestStep Next(HitTestAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        var attempt = ++Attempts;
        var last = attempt >= MaxAttempts;

        if (answer.Failure is { } failure)
        {
            if (last)
            {
                // The window went away or the browser is busy; treat as "no content" and let the coordinator fall back.
                LogComFailure(failure, process);
                return new HitTestStep(HitTestAction.Done, ForgetRoot: true);
            }

            // A browser whose accessibility tree is still being built answers with an error rather than an empty
            // tree. Take another root and keep trying; only the last attempt gives up.
            LogComRetry(failure, process);
            Query = _cursor;
            return new HitTestStep(HitTestAction.Reacquire, ForgetRoot: true);
        }

        var chain = ContentPath.ToPage(answer.Path);
        var document = chain.FirstOrDefault(n => n.Role == ContentRole.Document);

        // A Chromium tree left at the scale of an earlier pinch answers in its own, zoomed coordinates: ask again
        // where it believes the cursor is, then translate its answer back.
        var stale = document is null || gecko ? null : StalePageZoom.Detect(document.Bounds, answer.Window ?? document.Bounds);
        var expectedQuery = stale?.ToReported(_cursor) ?? _cursor;
        if (expectedQuery != Query && !last)
        {
            Query = expectedQuery;
            return new HitTestStep(HitTestAction.AskAgain);
        }

        var scale = stale?.Scale ?? 1;
        if (stale is { } zoom)
        {
            LogStaleZoom(process, zoom.Scale);
            chain = [.. chain.Select(n => n with { Bounds = zoom.ToActual(n.Bounds) })];
            document = chain.First(n => n.Role == ContentRole.Document);
        }

        // Anything narrower than the document means we're inside real content. Right after the wake-up the path
        // may instead end above the document or stop at a page-wide placeholder, because the renderer serializes
        // the tree asynchronously; give it a few frames.
        if (document is not null && chain.Any(n => n.Bounds.Width < document.Bounds.Width))
        {
            // The hit-test itself is fresh, but reported rectangles lag behind scrolling. A leaf whose rectangle
            // doesn't contain the point is stale: keep asking until the tree has caught up.
            if (chain[0].Bounds.Contains(_cursor))
                return Found(new ContentHit(chain, Viewport(document.Bounds, answer.Window), scale));

            if (!last)
                return new HitTestStep(HitTestAction.Wait);

            LogStaleBounds(process, chain[0].Bounds.Left, chain[0].Bounds.Top, _cursor.X, _cursor.Y);
            return new HitTestStep(HitTestAction.Done);
        }

        // A single handshake right after the window appeared can be too early; nudging again is cheap.
        if (!last)
            return new HitTestStep(HitTestAction.Wait, Nudge: !gecko);

        // Nothing better after ~600 ms: forget this root so the next press re-acquires and re-wakes it, and settle
        // for what there is.
        if (document is not null)
            return Found(new ContentHit(chain, Viewport(document.Bounds, answer.Window), scale), forgetRoot: true);

        // Some Chromium builds answer with a chain that never reaches a document node (seen with the native UI
        // Automation provider enabled), and a page still loading can answer with no chain at all. The render
        // window's rectangle is the page in every build, so it stands in for the document rather than the press
        // doing nothing. Once per failed press, so the description is cheap enough to build unconditionally.
        var path = chain.Count == 0 ? "(empty)" : ContentPath.Describe(chain);
        if (ContentPath.WithWindowAsPage(chain, answer.Window) is not { } withPage)
        {
            LogNoDocument(process, path);
            return new HitTestStep(HitTestAction.Done, ForgetRoot: true);
        }

        LogDocumentFromWindow(process, path);
        return Found(new ContentHit(withPage, Viewport(withPage[^1].Bounds, answer.Window), scale), forgetRoot: true);
    }

    private static HitTestStep Found(ContentHit hit, bool forgetRoot = false) => new(HitTestAction.Done, hit, forgetRoot);

    /// <summary>The visible page area: the document's rectangle, kept inside the window that shows it.</summary>
    /// <remarks>
    /// The document's reported bounds are normally the visible page area, but some Chromium documents (sidebar
    /// WebUI, background frames) report absurd rectangles, once a 7496 px wide "document" in a 1793 px window.
    /// The render window's own rectangle is always right, so the viewport is their intersection, or the window
    /// alone when the two don't overlap, or the document alone when the window could not be read.
    /// </remarks>
    /// <param name="document">The document's rectangle.</param>
    /// <param name="window">The render window's rectangle, or null when it could not be read.</param>
    /// <returns>The viewport.</returns>
    internal static PixelRect Viewport(PixelRect document, PixelRect? window)
    {
        if (window is not { } frame)
            return document;

        var intersection = document.Intersect(frame);
        return intersection.IsEmpty ? frame : intersection;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "No document node on the accessibility path in {Process}; the path was: {Path}")]
    private partial void LogNoDocument(string? process, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Process} exposed no document node; using the render window as the page. Path under the cursor: {Path}")]
    private partial void LogDocumentFromWindow(string? process, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Accessibility bounds in {Process} are stale (element at ({Left}, {Top}) does not contain the cursor at ({X}, {Y})); skipping this press rather than zooming the wrong place.")]
    private partial void LogStaleBounds(string? process, int left, int top, int x, int y);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The accessibility tree of {Process} still reports the page pinch-zoomed x{Scale:F3}; translating its coordinates.")]
    private partial void LogStaleZoom(string? process, double scale);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Accessibility call failed in {Process}; re-acquiring the tree and trying again.")]
    private partial void LogComRetry(Exception error, string? process);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Accessibility call failed in {Process}; dropping the cached root.")]
    private partial void LogComFailure(Exception exception, string? process);
}
