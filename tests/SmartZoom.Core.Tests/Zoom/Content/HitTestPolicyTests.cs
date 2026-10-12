using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.Core.Input;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Tests.Zoom.Content;

/// <summary>
/// What a browser's accessibility answers mean. Each class replays a sequence of answers a browser has been
/// seen to give, and the failure it caused before the rule that handles it existed.
/// </summary>
public sealed class HitTestPolicyTests
{
    // Measured in Brave on a 200% display: the render window, and a paragraph in it.
    private static readonly PixelRect Window = PixelRect.FromSize(1891, 85, 1793, 1527);
    private static readonly ScreenPoint Cursor = new(2700, 1100);
    private static readonly ContentNode Paragraph = Node(ContentRole.Text, PixelRect.FromSize(2300, 1050, 948, 103));
    private static readonly ContentNode Article = Node(ContentRole.Group, PixelRect.FromSize(2250, 300, 1100, 4000));
    private static readonly ContentNode Page = Node(ContentRole.Document, Window);

    // What sits above the page in Chromium's tree: the browser's own client area and window.
    private static readonly ContentNode BrowserClient = Node(ContentRole.Other, PixelRect.FromSize(1880, 0, 1815, 1620));

    private static ContentNode Node(ContentRole role, PixelRect bounds) => new(role, bounds);

    private static HitTestAnswer Answer(params ContentNode[] path) => new(path, Window);

    // The platform side passes on whatever the tree threw (a COMException, mostly); the policy treats every
    // failure alike, so any exception stands in for one here.
    private static HitTestAnswer Refused(string why) => HitTestAnswer.Refused(new InvalidOperationException(why));

    private static HitTestPolicy Chromium() => new(Cursor, gecko: false, "brave", NullLogger.Instance);

    private static HitTestPolicy Gecko() => new(Cursor, gecko: true, "firefox", NullLogger.Instance);

    /// <summary>Feeds the answers in turn, the last one repeated, until the policy is done.</summary>
    private static List<HitTestStep> Run(HitTestPolicy policy, params HitTestAnswer[] answers)
    {
        var steps = new List<HitTestStep>();
        for (var i = 0; i < 100; i++)
        {
            var step = policy.Next(answers[Math.Min(i, answers.Length - 1)]);
            steps.Add(step);
            if (step.Action == HitTestAction.Done)
                return steps;
        }

        throw new InvalidOperationException("The policy never finished.");
    }

    public sealed class Finding_content
    {
        [Fact]
        public void A_paragraph_on_the_page_is_found_at_the_first_answer()
        {
            var steps = Run(Chromium(), Answer(Paragraph, Article, Page, BrowserClient));

            var step = Assert.Single(steps);
            Assert.Equal(HitTestAction.Done, step.Action);
            Assert.False(step.ForgetRoot);
            Assert.Equal([Paragraph, Article, Page], step.Hit!.Chain);
            Assert.Equal(Window, step.Hit.Viewport);
            Assert.Equal(1, step.Hit.PageScale);
        }

        [Fact]
        public void A_document_that_reports_an_absurd_rectangle_is_kept_inside_its_window()
        {
            // A second Brave window's document once reported 7496 px wide, which put the anchor off the screen.
            var absurd = Node(ContentRole.Document, PixelRect.FromSize(1800, 85, 7496, 1527));

            var hit = Run(Chromium(), Answer(Paragraph, Article, absurd))[^1].Hit!;

            Assert.Equal(PixelRect.FromSize(1891, 85, 1793, 1527), hit.Viewport);
        }

        [Fact]
        public void Without_a_window_rectangle_the_document_is_the_viewport()
        {
            var hit = Run(Chromium(), new HitTestAnswer([Paragraph, Article, Page], null))[^1].Hit!;

            Assert.Equal(Window, hit.Viewport);
        }
    }

    public sealed class A_browser_still_building_its_tree
    {
        [Fact]
        public void A_tree_that_throws_is_taken_again_until_it_answers()
        {
            // A cold Chromium (just after a reboot) threw on its first accessibility calls; giving up at the first
            // error was the first press of the day doing nothing.
            var refused = Refused("not ready");
            var policy = Chromium();

            var steps = Run(policy, refused, refused, Answer(Paragraph, Article, Page));

            Assert.Equal([HitTestAction.Reacquire, HitTestAction.Reacquire, HitTestAction.Done], steps.Select(s => s.Action));
            Assert.True(steps[0].ForgetRoot);
            Assert.NotNull(steps[^1].Hit);
        }

        [Fact]
        public void An_answer_that_cannot_be_converted_is_a_refusal_like_any_other()
        {
            // Brave's accHitTest occasionally returns a VARIANT the runtime cannot convert (#22); handling only
            // COMException let that one end the press.
            var steps = Run(Chromium(), HitTestAnswer.Refused(new InvalidCastException()), Answer(Paragraph, Article, Page));

            Assert.Equal(HitTestAction.Reacquire, steps[0].Action);
            Assert.NotNull(steps[^1].Hit);
        }

        [Fact]
        public void A_tree_that_never_answers_gives_up_on_the_last_attempt_and_forgets_its_root()
        {
            var steps = Run(Chromium(), Refused("busy"));

            Assert.Equal(HitTestPolicy.MaxAttempts, steps.Count);
            Assert.Null(steps[^1].Hit);
            Assert.True(steps[^1].ForgetRoot);
        }

        [Fact]
        public void A_refusal_sends_the_next_question_back_to_the_cursor()
        {
            var stale = Node(ContentRole.Document, PixelRect.FromSize(1782, -383, 2344, 1996));
            var policy = Chromium();

            policy.Next(Answer(stale));
            Assert.NotEqual(Cursor, policy.Query);

            policy.Next(Refused("gone"));
            Assert.Equal(Cursor, policy.Query);
        }

        [Fact]
        public void A_page_wide_placeholder_is_waited_for_and_Chromium_is_nudged_meanwhile()
        {
            // Right after the wake-up the renderer has not serialized the tree yet: the path stops at a group as
            // wide as the page.
            var placeholder = Node(ContentRole.Group, Window);

            var steps = Run(Chromium(), Answer(placeholder, Page), Answer(placeholder, Page), Answer(Paragraph, Article, Page));

            Assert.Equal([HitTestAction.Wait, HitTestAction.Wait, HitTestAction.Done], steps.Select(s => s.Action));
            Assert.All(steps.Take(2), s => Assert.True(s.Nudge));
            Assert.Equal(Paragraph, steps[^1].Hit!.Chain[0]);
        }

        [Fact]
        public void Firefox_is_never_nudged_because_its_tree_is_complete_from_the_first_call()
        {
            var steps = Run(Gecko(), Answer(Page), Answer(Paragraph, Article, Page));

            Assert.Equal(HitTestAction.Wait, steps[0].Action);
            Assert.False(steps[0].Nudge);
        }

        [Fact]
        public void A_placeholder_that_never_resolves_settles_for_the_page_and_forgets_the_root()
        {
            var steps = Run(Chromium(), Answer(Node(ContentRole.Group, Window), Page));

            Assert.Equal(HitTestPolicy.MaxAttempts, steps.Count);
            Assert.Equal(Page, steps[^1].Hit!.Chain[^1]);
            Assert.True(steps[^1].ForgetRoot);
        }
    }

    public sealed class A_page_made_of_frames
    {
        [Fact]
        public void The_outermost_document_is_the_page_and_a_frame_is_a_box_on_it()
        {
            // MSN: a card's text fills its 656 px frame. Stopping at the frame's document made the frame the
            // viewport and the paragraph "wider than the page", and the press did nothing.
            var text = Node(ContentRole.Text, PixelRect.FromSize(2400, 1080, 631, 37));
            var frame = Node(ContentRole.Document, PixelRect.FromSize(2390, 700, 656, 980));

            var hit = Run(Chromium(), Answer(text, frame, Article, Page, BrowserClient))[^1].Hit!;

            Assert.Equal([ContentRole.Text, ContentRole.Group, ContentRole.Group, ContentRole.Document], hit.Chain.Select(n => n.Role));
            Assert.Equal(Window, hit.Viewport);
        }
    }

    public sealed class A_tree_left_pinch_zoomed
    {
        // The document Brave kept reporting after a zoom near the bottom edge had been undone: x1.307.
        private static readonly ContentNode StaleDocument = Node(ContentRole.Document, PixelRect.FromSize(1782, -383, 2344, 1996));

        // The 948 px paragraph as the stale tree reported it.
        private static readonly ContentNode StaleParagraph = Node(ContentRole.Text, PixelRect.FromSize(2300, 900, 1240, 103));

        [Fact]
        public void The_tree_is_asked_again_where_it_believes_the_cursor_is()
        {
            var policy = Chromium();

            var step = policy.Next(Answer(StaleDocument));

            Assert.Equal(HitTestAction.AskAgain, step.Action);
            Assert.Equal(StalePageZoom.Detect(StaleDocument.Bounds, Window)!.Value.ToReported(Cursor), policy.Query);
        }

        [Fact]
        public void Its_answer_is_translated_back_to_the_screen_and_the_scale_is_reported()
        {
            // Before the translation every press after such a zoom hit-tested the wrong spot: wrong block, wrong
            // scale, or "no zoomable block".
            var steps = Run(Chromium(), Answer(StaleDocument), Answer(StaleParagraph, StaleDocument));

            var hit = steps[^1].Hit!;
            Assert.Equal(948, hit.Chain[0].Bounds.Width, tolerance: 1);
            Assert.Equal(Window, hit.Chain[^1].Bounds);
            Assert.Equal(1.307, hit.PageScale, precision: 3);
        }

        [Fact]
        public void Firefox_is_never_read_as_stale()
        {
            var policy = Gecko();

            var step = policy.Next(Answer(Paragraph, StaleDocument));

            Assert.Equal(HitTestAction.Done, step.Action);
            Assert.Equal(1, step.Hit!.PageScale);
            Assert.Equal(Cursor, policy.Query);
        }
    }

    public sealed class Bounds_that_lag_behind_a_scroll
    {
        // The tree's rectangles lag behind scrolling: the leaf under the hit-test does not hold the cursor.
        private static readonly ContentNode Behind = Node(ContentRole.Text, PixelRect.FromSize(2300, 1400, 948, 103));

        [Fact]
        public void A_leaf_that_does_not_hold_the_cursor_is_waited_for()
        {
            var steps = Run(Chromium(), Answer(Behind, Article, Page), Answer(Paragraph, Article, Page));

            Assert.Equal([HitTestAction.Wait, HitTestAction.Done], steps.Select(s => s.Action));
            Assert.False(steps[0].Nudge);
            Assert.Equal(Paragraph, steps[^1].Hit!.Chain[0]);
        }

        [Fact]
        public void If_it_never_catches_up_the_press_zooms_nothing_rather_than_the_wrong_place()
        {
            var steps = Run(Chromium(), Answer(Behind, Article, Page));

            Assert.Equal(HitTestPolicy.MaxAttempts, steps.Count);
            Assert.Null(steps[^1].Hit);
            Assert.False(steps[^1].ForgetRoot);
        }
    }

    public sealed class A_path_without_a_document
    {
        [Fact]
        public void The_render_window_stands_in_for_the_page_on_the_last_attempt()
        {
            // A second PC's Chrome and Edge answered every press with a chain that never reached a document
            // node, and every press did nothing (0.1.1).
            var steps = Run(Chromium(), Answer(Paragraph, Article));

            Assert.Equal(HitTestPolicy.MaxAttempts, steps.Count);
            var hit = steps[^1].Hit!;
            Assert.Equal(new ContentNode(ContentRole.Document, Window), hit.Chain[^1]);
            Assert.Equal(Window, hit.Viewport);
            Assert.True(steps[^1].ForgetRoot);
        }

        [Fact]
        public void A_page_still_loading_that_answers_nothing_still_gets_a_page()
        {
            // #6: a page still loading answered every attempt with nothing at all.
            var hit = Run(Chromium(), Answer())[^1].Hit!;

            Assert.Equal([new ContentNode(ContentRole.Document, Window)], hit.Chain);
        }

        [Fact]
        public void Without_a_window_either_there_is_nothing_to_zoom()
        {
            var steps = Run(Chromium(), new HitTestAnswer([Paragraph, Article], null));

            Assert.Null(steps[^1].Hit);
            Assert.True(steps[^1].ForgetRoot);
        }
    }
}
