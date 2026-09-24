using SmartZoom.Core.Input;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Tests.Zoom.Content;

public sealed class StalePageZoomTests
{
    // Measured in Brave on a 200% display: the render window, and the document rectangle its accessibility
    // tree kept reporting after a zoom near the bottom edge had been undone.
    private static readonly PixelRect Window = PixelRect.FromSize(1891, 85, 1793, 1527);
    private static readonly PixelRect StaleDocument = PixelRect.FromSize(1782, -383, 2344, 1996);

    public sealed class Detecting_a_stale_zoom
    {
        [Fact]
        public void Document_that_matches_its_window_is_not_stale() =>
            Assert.Null(StalePageZoom.Detect(Window, Window));

        [Fact]
        public void Document_a_scrollbar_narrower_than_its_window_is_not_stale() =>
            Assert.Null(StalePageZoom.Detect(Window with { Right = Window.Right - 34 }, Window));

        [Fact]
        public void Uniformly_enlarged_document_covering_the_window_is_a_stale_zoom()
        {
            var zoom = StalePageZoom.Detect(StaleDocument, Window);

            Assert.NotNull(zoom);
            Assert.Equal(1.307, zoom.Value.Scale, precision: 3);
        }

        [Fact]
        public void Oversized_document_of_another_shape_is_not_a_zoom() =>
            Assert.Null(StalePageZoom.Detect(PixelRect.FromSize(1800, 0, 7496, 1527), Window));

        [Fact]
        public void Enlarged_document_that_does_not_cover_the_window_is_not_a_zoom() =>
            Assert.Null(StalePageZoom.Detect(PixelRect.FromSize(2400, 85, 2344, 1996), Window));

        [Fact]
        public void Empty_rectangles_are_not_a_zoom()
        {
            Assert.Null(StalePageZoom.Detect(default, Window));
            Assert.Null(StalePageZoom.Detect(StaleDocument, default));
        }
    }

    public sealed class Translating_back
    {
        private readonly StalePageZoom _zoom = StalePageZoom.Detect(StaleDocument, Window)!.Value;

        [Fact]
        public void The_document_maps_onto_the_window() =>
            Assert.Equal(Window, _zoom.ToActual(StaleDocument));

        [Fact]
        public void A_block_shrinks_back_to_its_real_size()
        {
            // The 948 px wide paragraph was reported 1240 px wide.
            var actual = _zoom.ToActual(PixelRect.FromSize(2300, 900, 1240, 103));

            Assert.Equal(948, actual.Width, tolerance: 1);
            Assert.Equal(79, actual.Height, tolerance: 1);
        }

        [Fact]
        public void A_point_survives_the_round_trip()
        {
            var cursor = new ScreenPoint(2633, 904);
            var reported = _zoom.ToReported(cursor);
            var back = _zoom.ToActual(new PixelRect(reported.X, reported.Y, reported.X + 1, reported.Y + 1));

            Assert.Equal(new ScreenPoint(2752, 688), reported);
            Assert.InRange(back.Left, cursor.X - 1, cursor.X + 1);
            Assert.InRange(back.Top, cursor.Y - 1, cursor.Y + 1);
        }

        [Fact]
        public void The_window_origin_maps_to_the_reported_document_origin() =>
            Assert.Equal(new ScreenPoint(StaleDocument.Left, StaleDocument.Top), _zoom.ToReported(new ScreenPoint(Window.Left, Window.Top)));
    }
}
