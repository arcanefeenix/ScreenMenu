using LedMenu.Core.Calibration;
using LedMenu.Core.Screens;

namespace LedMenu.Core.Tests;

public class PixelBufferTests
{
    [Fact]
    public void New_buffer_is_opaque_black()
    {
        var b = new PixelBuffer(4, 3);
        Assert.Equal(4 * 3 * 4, b.Data.Length);
        for (var y = 0; y < 3; y++)
            for (var x = 0; x < 4; x++)
                Assert.Equal(Rgb.Black, b.GetPixel(x, y));
        Assert.All(Enumerable.Range(0, 12).Select(i => b.Data[i * 4 + 3]), a => Assert.Equal(255, a));
    }

    [Fact]
    public void Pixels_are_stored_as_BGRA()
    {
        var b = new PixelBuffer(2, 1);
        b.SetPixel(1, 0, new Rgb(10, 20, 30));
        Assert.Equal(new byte[] { 30, 20, 10, 255 }, b.Data[4..8]);
        Assert.Equal(new Rgb(10, 20, 30), b.GetPixel(1, 0));
    }

    [Fact]
    public void Drawing_outside_the_buffer_is_clipped_not_an_error()
    {
        var b = new PixelBuffer(10, 10);
        b.FillRect(-5, -5, 8, 8, Rgb.Red);
        b.FillRect(8, 8, 50, 50, Rgb.Green);
        b.SetPixel(-1, 3, Rgb.White);
        b.SetPixel(3, 99, Rgb.White);
        Assert.Equal(Rgb.Red, b.GetPixel(0, 0));
        Assert.Equal(Rgb.Red, b.GetPixel(2, 2));
        Assert.Equal(Rgb.Black, b.GetPixel(3, 3));
        Assert.Equal(Rgb.Green, b.GetPixel(9, 9));
        Assert.Equal(Rgb.Black, b.GetPixel(7, 7));
    }

    [Fact]
    public void Frame_covers_exactly_the_outer_ring()
    {
        var b = new PixelBuffer(5, 4);
        b.Frame(0, 0, 5, 4, Rgb.White, 1);
        var lit = 0;
        for (var y = 0; y < 4; y++)
            for (var x = 0; x < 5; x++)
            {
                var edge = x == 0 || y == 0 || x == 4 || y == 3;
                Assert.Equal(edge ? Rgb.White : Rgb.Black, b.GetPixel(x, y));
                if (edge) lit++;
            }
        Assert.Equal(14, lit);
    }

    [Fact]
    public void Lines_are_one_pixel_wide()
    {
        var b = new PixelBuffer(8, 8);
        b.Line(0, 0, 7, 7, Rgb.White);
        for (var i = 0; i < 8; i++) Assert.Equal(Rgb.White, b.GetPixel(i, i));
        var count = 0;
        for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++) if (b.GetPixel(x, y) == Rgb.White) count++;
        Assert.Equal(8, count);
    }

    [Fact]
    public void Circle_hits_the_four_extremes()
    {
        var b = new PixelBuffer(41, 41);
        b.Circle(20, 20, 10, Rgb.White);
        Assert.Equal(Rgb.White, b.GetPixel(30, 20));
        Assert.Equal(Rgb.White, b.GetPixel(10, 20));
        Assert.Equal(Rgb.White, b.GetPixel(20, 30));
        Assert.Equal(Rgb.White, b.GetPixel(20, 10));
        Assert.Equal(Rgb.Black, b.GetPixel(20, 20));
    }

    [Fact]
    public void Blit_clips_on_all_sides()
    {
        var src = new PixelBuffer(4, 4);
        src.Clear(Rgb.Red);
        var dst = new PixelBuffer(6, 6);
        dst.Blit(src, -2, -2);
        dst.Blit(src, 4, 4);
        Assert.Equal(Rgb.Red, dst.GetPixel(0, 0));
        Assert.Equal(Rgb.Red, dst.GetPixel(1, 1));
        Assert.Equal(Rgb.Black, dst.GetPixel(2, 2));
        Assert.Equal(Rgb.Red, dst.GetPixel(5, 5));
        Assert.Equal(Rgb.Black, dst.GetPixel(3, 3));
        dst.Blit(src, 100, 100);   // entirely outside: no effect, no exception
    }

    [Fact]
    public void Text_uses_whole_pixel_squares_at_the_requested_scale()
    {
        var b = new PixelBuffer(30, 20);
        b.DrawText("I", 2, 3, 1, Rgb.White);
        // glyph "I" row 0 is ".###." -> pixels x = 3..5 at y = 3
        Assert.Equal(Rgb.Black, b.GetPixel(2, 3));
        Assert.Equal(Rgb.White, b.GetPixel(3, 3));
        Assert.Equal(Rgb.White, b.GetPixel(5, 3));
        Assert.Equal(Rgb.Black, b.GetPixel(6, 3));

        var big = new PixelBuffer(30, 20);
        big.DrawText("I", 2, 3, 2, Rgb.White);
        // scale 2 doubles each glyph pixel into a 2x2 block
        Assert.Equal(Rgb.White, big.GetPixel(4, 3));
        Assert.Equal(Rgb.White, big.GetPixel(5, 4));
        Assert.Equal(Rgb.White, big.GetPixel(9, 3));
        Assert.Equal(Rgb.Black, big.GetPixel(10, 3));
        Assert.Equal(Rgb.Black, big.GetPixel(3, 3));
    }

    [Fact]
    public void Text_metrics_follow_advance_and_scale()
    {
        Assert.Equal(0, PixelFont.TextWidth("", 3));
        Assert.Equal(5, PixelFont.TextWidth("A", 1));
        Assert.Equal(11, PixelFont.TextWidth("AB", 1));
        Assert.Equal(22, PixelFont.TextWidth("AB", 2));
        Assert.Equal(21, PixelFont.TextHeight(3));
        Assert.Equal(3, PixelFont.FitScale("AB", 40, 3));
        Assert.Equal(1, PixelFont.FitScale("ABCDEFGHIJ", 40, 3));
    }
}

public class PixelFontTests
{
    [Fact]
    public void Every_glyph_is_5_by_7_of_dots_and_hashes()
    {
        foreach (var c in PixelFont.SupportedCharacters)
        {
            var g = PixelFont.Glyph(c);
            Assert.Equal(PixelFont.GlyphHeight, g.Length);
            Assert.All(g, row =>
            {
                Assert.Equal(PixelFont.GlyphWidth, row.Length);
                Assert.All(row, ch => Assert.True(ch is '.' or '#', $"glyph '{c}' has '{ch}'"));
            });
        }
    }

    [Fact]
    public void Digits_letters_and_calibration_symbols_exist()
    {
        foreach (var c in "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ -.,:()/=+#%_?") Assert.True(PixelFont.HasGlyph(c), c.ToString());
    }

    [Fact]
    public void Lower_case_and_the_multiplication_sign_map_to_existing_glyphs()
    {
        Assert.Equal(PixelFont.Glyph('A'), PixelFont.Glyph('a'));
        Assert.Equal(PixelFont.Glyph('X'), PixelFont.Glyph('×'));
        Assert.Equal(PixelFont.Glyph('?'), PixelFont.Glyph('€'));   // unsupported character
    }

    [Fact]
    public void Distinct_characters_have_distinct_shapes()
    {
        var shapes = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(c => string.Join("/", PixelFont.Glyph(c))).ToList();
        Assert.Equal(shapes.Count, shapes.Distinct().Count());
    }
}

public class ScreenPatternTests
{
    private static CalibrationScreen S(int w, int h, int n = 1, string name = "Food Menu", int x = 0, int y = 0) =>
        new(Guid.NewGuid(), n, name, x, y, w, h);

    private static bool RingIsPure(PixelBuffer b, Rgb c)
    {
        for (var x = 0; x < b.Width; x++) if (b.GetPixel(x, 0) != c || b.GetPixel(x, b.Height - 1) != c) return false;
        for (var y = 0; y < b.Height; y++) if (b.GetPixel(0, y) != c || b.GetPixel(b.Width - 1, y) != c) return false;
        return true;
    }

    [Theory]
    [InlineData(336, 672)]
    [InlineData(168, 336)]
    [InlineData(1176, 672)]
    [InlineData(1176, 336)]
    [InlineData(101, 51)]
    [InlineData(64, 64)]
    public void Calibration_is_generated_at_the_screens_own_size(int w, int h)
    {
        var b = ScreenPatterns.Calibration(S(w, h));
        Assert.Equal((w, h), (b.Width, b.Height));
        Assert.True(RingIsPure(b, Rgb.White), "outer 1-pixel boundary must be pure white all the way round");
    }

    [Fact]
    public void Tiny_and_odd_screens_never_throw()
    {
        foreach (var (w, h) in new[] { (1, 1), (2, 2), (3, 7), (5, 5), (10, 400), (400, 10) })
        {
            var cal = ScreenPatterns.Calibration(S(w, h));
            var id = ScreenPatterns.Identify(S(w, h));
            Assert.Equal((w, h), (cal.Width, cal.Height));
            Assert.Equal((w, h), (id.Width, id.Height));
        }
    }

    [Fact]
    public void Outer_boundary_is_exactly_one_pixel_thick()
    {
        var b = ScreenPatterns.Calibration(S(336, 672));
        // one pixel in from the edge, away from corner markers and text, nothing is white
        Assert.NotEqual(Rgb.White, b.GetPixel(1, 300));
        Assert.NotEqual(Rgb.White, b.GetPixel(334, 300));
        Assert.NotEqual(Rgb.White, b.GetPixel(100, 1));
        Assert.NotEqual(Rgb.White, b.GetPixel(100, 670));
    }

    [Fact]
    public void Even_sized_screen_has_two_pixel_centre_lines_on_the_middle()
    {
        var b = ScreenPatterns.Calibration(S(336, 672));
        // vertical: columns 167 and 168 at rows clear of text; neighbours are not centre-line colour
        foreach (var y in new[] { 3, 668 })
        {
            Assert.Equal(Rgb.Magenta, b.GetPixel(167, y));
            Assert.Equal(Rgb.Magenta, b.GetPixel(168, y));
            Assert.NotEqual(Rgb.Magenta, b.GetPixel(166, y));
            Assert.NotEqual(Rgb.Magenta, b.GetPixel(169, y));
        }
        // horizontal: rows 335 and 336
        foreach (var x in new[] { 3, 332 })
        {
            Assert.Equal(Rgb.Magenta, b.GetPixel(x, 335));
            Assert.Equal(Rgb.Magenta, b.GetPixel(x, 336));
            Assert.NotEqual(Rgb.Magenta, b.GetPixel(x, 334));
            Assert.NotEqual(Rgb.Magenta, b.GetPixel(x, 337));
        }
    }

    [Fact]
    public void Odd_sized_screen_has_a_single_pixel_centre_line()
    {
        var b = ScreenPatterns.Calibration(S(101, 51));
        Assert.Equal(Rgb.Magenta, b.GetPixel(50, 2));
        Assert.NotEqual(Rgb.Magenta, b.GetPixel(49, 2));
        Assert.NotEqual(Rgb.Magenta, b.GetPixel(51, 2));
    }

    [Fact]
    public void Corner_markers_have_one_colour_each()
    {
        var b = ScreenPatterns.Calibration(S(336, 672));
        Assert.Equal(Rgb.Red, b.GetPixel(1, 1));
        Assert.Equal(Rgb.Green, b.GetPixel(334, 1));
        Assert.Equal(Rgb.Blue, b.GetPixel(1, 670));
        Assert.Equal(Rgb.Yellow, b.GetPixel(334, 670));
        // markers are 20x20 at this size
        Assert.Equal(Rgb.Red, b.GetPixel(19, 19));
        Assert.NotEqual(Rgb.Red, b.GetPixel(20, 19));
    }

    [Fact]
    public void Different_sizes_are_drawn_natively_not_scaled_from_one_image()
    {
        var big = ScreenPatterns.Calibration(S(336, 672));
        var small = ScreenPatterns.Calibration(S(168, 336));
        Assert.Equal((168, 336), (small.Width, small.Height));
        // centre lines sit on each screen's own middle
        Assert.Equal(Rgb.Magenta, small.GetPixel(83, 3));
        Assert.Equal(Rgb.Magenta, small.GetPixel(84, 3));
        // and a 1-pixel boundary stays 1 pixel at both sizes
        Assert.NotEqual(Rgb.White, small.GetPixel(1, 150));
        Assert.NotEqual(Rgb.White, big.GetPixel(1, 150));
    }

    [Fact]
    public void Patterns_are_deterministic_and_depend_on_name_number_and_position()
    {
        var a1 = ScreenPatterns.Calibration(S(336, 672, 1, "Food Menu", 0, 0));
        var a2 = ScreenPatterns.Calibration(S(336, 672, 1, "Food Menu", 0, 0));
        Assert.Equal(a1.Data, a2.Data);
        Assert.NotEqual(a1.Data, ScreenPatterns.Calibration(S(336, 672, 1, "Drink Menu", 0, 0)).Data);
        Assert.NotEqual(a1.Data, ScreenPatterns.Calibration(S(336, 672, 2, "Food Menu", 0, 0)).Data);
        Assert.NotEqual(a1.Data, ScreenPatterns.Calibration(S(336, 672, 1, "Food Menu", 336, 0)).Data);
    }

    [Fact]
    public void Calibration_text_is_drawn_with_the_name_and_size()
    {
        var b = ScreenPatterns.Calibration(S(336, 672, 1, "Food Menu"));
        Assert.True(CountColor(b, Rgb.Yellow, 40, 40, 296, 300) > 100, "name text (yellow) missing");
        Assert.True(CountColor(b, new Rgb(0, 255, 255), 10, 40, 316, 300) > 50, "output position text (cyan) missing");
    }

    [Fact]
    public void Identify_shows_a_coloured_background_number_name_and_size()
    {
        var b = ScreenPatterns.Identify(S(336, 672, 2, "Drink Menu", 336, 0));
        Assert.True(RingIsPure(b, Rgb.White));
        Assert.NotEqual(Rgb.Black, b.GetPixel(10, 10));                       // tinted background, not black
        Assert.True(CountColor(b, Rgb.White, 20, 20, 296, 600) > 400, "SCREEN and number text missing");
        Assert.True(CountColor(b, Rgb.Yellow, 20, 20, 296, 600) > 100, "name text missing");
    }

    [Fact]
    public void Long_names_are_shortened_to_fit_instead_of_running_off_the_screen()
    {
        var b = ScreenPatterns.Identify(S(168, 336, 1, "The Extremely Long Festival Food And Drinks Menu"));
        Assert.True(RingIsPure(b, Rgb.White));
        Assert.Equal((168, 336), (b.Width, b.Height));
    }

    [Fact]
    public void Number_colours_cycle_and_handle_any_number()
    {
        Assert.Equal(ScreenPatterns.ColorFor(1), ScreenPatterns.ColorFor(7));
        Assert.NotEqual(ScreenPatterns.ColorFor(1), ScreenPatterns.ColorFor(2));
        _ = ScreenPatterns.ColorFor(0);
        _ = ScreenPatterns.ColorFor(-3);
    }

    [Theory]
    [InlineData(336, 167, 2)]
    [InlineData(672, 335, 2)]
    [InlineData(101, 50, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(2, 0, 2)]
    public void Centre_span_is_the_middle_pixel_or_pair(int n, int start, int length) =>
        Assert.Equal((start, length), ScreenPatterns.CenterSpan(n));

    internal static int CountColor(PixelBuffer b, Rgb c, int x, int y, int w, int h)
    {
        var n = 0;
        for (var yy = y; yy < Math.Min(b.Height, y + h); yy++)
            for (var xx = x; xx < Math.Min(b.Width, x + w); xx++)
                if (b.GetPixel(xx, yy) == c) n++;
        return n;
    }
}

public class CanvasPatternTests
{
    private static readonly PixelBuffer P1080 = CanvasPattern.Render(1920, 1080);

    private static bool RingIsPure(PixelBuffer b, Rgb c)
    {
        for (var x = 0; x < b.Width; x++) if (b.GetPixel(x, 0) != c || b.GetPixel(x, b.Height - 1) != c) return false;
        for (var y = 0; y < b.Height; y++) if (b.GetPixel(0, y) != c || b.GetPixel(b.Width - 1, y) != c) return false;
        return true;
    }

    [Fact]
    public void Has_the_exact_output_size_and_a_pure_one_pixel_boundary()
    {
        Assert.Equal((1920, 1080), (P1080.Width, P1080.Height));
        Assert.True(RingIsPure(P1080, Rgb.White));
        Assert.NotEqual(Rgb.White, P1080.GetPixel(1, 300));
        Assert.NotEqual(Rgb.White, P1080.GetPixel(300, 1));
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(3840, 2160)]
    [InlineData(1280, 720)]
    [InlineData(1176, 672)]
    [InlineData(800, 600)]
    [InlineData(336, 672)]
    [InlineData(101, 51)]
    [InlineData(1, 1)]
    [InlineData(7, 3)]
    public void Renders_any_canvas_size_without_error(int w, int h)
    {
        var b = CanvasPattern.Render(w, h);
        Assert.Equal((w, h), (b.Width, b.Height));
        Assert.True(RingIsPure(b, Rgb.White));
    }

    [Fact]
    public void Edge_bands_identify_each_edge_by_colour()
    {
        for (var i = 1; i <= 4; i++)
        {
            Assert.Equal(CanvasPattern.TopBand, P1080.GetPixel(300, i));
            Assert.Equal(CanvasPattern.LeftBand, P1080.GetPixel(i, 300));
            Assert.Equal(CanvasPattern.RightBand, P1080.GetPixel(1919 - i, 300));
            Assert.Equal(CanvasPattern.BottomBand, P1080.GetPixel(300, 1079 - i));
        }
        Assert.NotEqual(CanvasPattern.TopBand, P1080.GetPixel(300, 5));
    }

    [Fact]
    public void Nine_anchor_points_are_the_corners_edge_midpoints_and_centre()
    {
        var a = CanvasPattern.Anchors(1920, 1080);
        Assert.Equal(new[]
        {
            "TOP LEFT", "TOP CENTER", "TOP RIGHT", "CENTER LEFT", "CENTER", "CENTER RIGHT",
            "BOTTOM LEFT", "BOTTOM CENTER", "BOTTOM RIGHT",
        }, a.Select(x => x.Name));
        Assert.Equal(new[] { (0, 0), (960, 0), (1919, 0), (0, 540), (960, 540), (1919, 540), (0, 1079), (960, 1079), (1919, 1079) },
            a.Select(x => (x.X, x.Y)));
        Assert.Equal(9, a.Select(x => x.Color).Distinct().Count());
    }

    [Fact]
    public void Each_anchor_has_a_coloured_marker_at_its_position()
    {
        var anchors = CanvasPattern.Anchors(1920, 1080);
        for (var i = 0; i < 9; i++)
        {
            if (i == 4) continue;
            var (mx, my) = CanvasPattern.MarkerOrigin(i, 1920, 1080);
            Assert.Equal(anchors[i].Color, P1080.GetPixel(mx + 8, my + 8));
            Assert.Equal(anchors[i].Color, P1080.GetPixel(mx, my));
            Assert.Equal(anchors[i].Color, P1080.GetPixel(mx + 15, my + 15));
        }
        // the corner markers really are in the corners
        Assert.Equal((6, 6), CanvasPattern.MarkerOrigin(0, 1920, 1080));
        Assert.Equal((1920 - 6 - 16, 1080 - 6 - 16), CanvasPattern.MarkerOrigin(8, 1920, 1080));
    }

    [Fact]
    public void Centre_lines_sit_on_the_middle_pixels()
    {
        foreach (var y in new[] { 130, 1000 })
        {
            Assert.Equal(Rgb.Magenta, P1080.GetPixel(959, y));
            Assert.Equal(Rgb.Magenta, P1080.GetPixel(960, y));
            Assert.NotEqual(Rgb.Magenta, P1080.GetPixel(958, y));
            Assert.NotEqual(Rgb.Magenta, P1080.GetPixel(961, y));
        }
        foreach (var x in new[] { 300, 1600 })
        {
            Assert.Equal(Rgb.Magenta, P1080.GetPixel(x, 539));
            Assert.Equal(Rgb.Magenta, P1080.GetPixel(x, 540));
            Assert.NotEqual(Rgb.Magenta, P1080.GetPixel(x, 538));
            Assert.NotEqual(Rgb.Magenta, P1080.GetPixel(x, 541));
        }
    }

    [Fact]
    public void One_pixel_detail_blocks_alternate_every_single_pixel()
    {
        var blocks = CanvasPattern.TestBlocks(1920, 1080);
        Assert.Equal(3, blocks.Count);
        foreach (var b in blocks)
            for (var y = 0; y < b.Height; y++)
                for (var x = 0; x < b.Width; x++)
                {
                    var expectedOn = b.Kind switch
                    {
                        TestBlockKind.VerticalStripes => x % 2 == 0,
                        TestBlockKind.HorizontalStripes => y % 2 == 0,
                        _ => (x + y) % 2 == 0,
                    };
                    Assert.Equal(expectedOn ? Rgb.White : Rgb.Black, P1080.GetPixel(b.X + x, b.Y + y));
                }
    }

    [Fact]
    public void Grid_coordinate_labels_are_real_text_at_each_hundred_pixels()
    {
        // "500,400" is drawn in yellow with its first glyph's top-left at (503, 403)
        var expected = new PixelBuffer(60, 12);
        expected.DrawText("500,400", 0, 0, 1, new Rgb(255, 255, 0));
        for (var y = 0; y < 7; y++)
            for (var x = 0; x < PixelFont.TextWidth("500,400", 1); x++)
                if (expected.GetPixel(x, y) == new Rgb(255, 255, 0))
                    Assert.Equal(new Rgb(255, 255, 0), P1080.GetPixel(503 + x, 403 + y));
    }

    [Fact]
    public void Every_second_grid_label_is_double_size_for_photos()
    {
        var yellow = new Rgb(255, 255, 0);
        var expected = new PixelBuffer(120, 20);
        expected.DrawText("400,400", 0, 0, 2, yellow);
        for (var y = 0; y < 14; y++)
            for (var x = 0; x < PixelFont.TextWidth("400,400", 2); x++)
                if (expected.GetPixel(x, y) == yellow)
                    Assert.Equal(yellow, P1080.GetPixel(403 + x, 403 + y));
    }

    [Fact]
    public void Ruler_numbers_label_source_coordinates_along_the_edges()
    {
        // "500" centred on x=500 at y=28 along the top edge
        var width = PixelFont.TextWidth("500", 1);
        var left = 500 - width / 2;
        var lit = 0;
        for (var y = 28; y < 35; y++) for (var x = left; x < left + width; x++) if (P1080.GetPixel(x, y) == Rgb.White) lit++;
        Assert.True(lit > 15);
    }

    [Fact]
    public void Cropped_view_still_shows_enough_labels_to_find_its_origin()
    {
        // pretend the controller shows only a 1176x672 window whose top-left is source pixel (372, 204)
        const int ox = 372, oy = 204, cw = 1176, ch = 672;
        var yellow = new Rgb(255, 255, 0);
        var labelsSeen = 0;
        for (var gx = 400; gx <= 1500; gx += 100)
            for (var gy = 300; gy <= 800; gy += 100)
            {
                var px = gx + 3 - ox;
                var py = gy + 3 - oy;
                if (px < 0 || py < 0 || px + 20 > cw || py + 7 > ch) continue;
                var any = false;
                for (var y = 0; y < 7 && !any; y++)
                    for (var x = 0; x < 20 && !any; x++)
                        any = P1080.GetPixel(gx + 3 + x, gy + 3 + y) == yellow;
                if (any) labelsSeen++;
            }
        Assert.True(labelsSeen >= 40, $"only {labelsSeen} labels visible in the crop");
    }

    [Fact]
    public void Is_deterministic_and_differs_between_canvas_sizes()
    {
        Assert.Equal(P1080.Data, CanvasPattern.Render(1920, 1080).Data);
        Assert.NotEqual(P1080.Data.Length, CanvasPattern.Render(1176, 672).Data.Length);
    }
}

public class FrameComposerTests
{
    private static CalibrationScreen S(int n, int x, int y, int w, int h, string name = "Screen") =>
        new(Guid.NewGuid(), n, name, x, y, w, h);

    private static int NonBlack(PixelBuffer b, int x0 = 0, int y0 = 0, int? w = null, int? h = null)
    {
        var n = 0;
        for (var y = y0; y < y0 + (h ?? b.Height); y++)
            for (var x = x0; x < x0 + (w ?? b.Width); x++)
                if (b.GetPixel(x, y) != Rgb.Black) n++;
        return n;
    }

    [Fact]
    public void Normal_output_is_pure_black_even_when_screens_exist()
    {
        var f = FrameComposer.Compose(OutputMode.Normal, 1920, 1080, new[] { S(1, 0, 0, 336, 672) });
        Assert.Equal((1920, 1080), (f.Width, f.Height));
        Assert.Equal(0, NonBlack(f));
    }

    [Theory]
    [InlineData(OutputMode.IdentifyScreens)]
    [InlineData(OutputMode.ScreenCalibration)]
    public void Screen_modes_draw_only_inside_the_screens_and_leave_the_rest_black(OutputMode mode)
    {
        var s1 = S(1, 0, 0, 336, 672, "Food");
        var s2 = S(2, 400, 100, 200, 300, "Drink");
        var f = FrameComposer.Compose(mode, 1920, 1080, new[] { s1, s2 });
        Assert.Equal(0, NonBlack(f, 336, 0, 64, 1080));            // gap between screens
        Assert.Equal(0, NonBlack(f, 700, 0, 1220, 1080));          // right of both
        Assert.Equal(0, NonBlack(f, 0, 672, 1920, 408));           // below both
        Assert.True(NonBlack(f, 0, 0, 336, 672) > 1000);
        Assert.True(NonBlack(f, 400, 100, 200, 300) > 500);
    }

    [Fact]
    public void Each_screen_region_equals_its_own_native_size_pattern_exactly()
    {
        var s1 = S(1, 0, 0, 336, 672, "Food");
        var s2 = S(2, 336, 0, 336, 672, "Drink");
        var f = FrameComposer.Compose(OutputMode.ScreenCalibration, 1920, 1080, new[] { s1, s2 });
        foreach (var s in new[] { s1, s2 })
        {
            var p = ScreenPatterns.Calibration(s);
            for (var y = 0; y < s.Height; y++)
                for (var x = 0; x < s.Width; x++)
                    Assert.Equal(p.GetPixel(x, y), f.GetPixel(s.X + x, s.Y + y));
        }
    }

    [Fact]
    public void Screen_at_an_offset_has_its_boundary_on_exactly_those_pixels()
    {
        var f = FrameComposer.Compose(OutputMode.ScreenCalibration, 1920, 1080, new[] { S(1, 372, 204, 336, 672) });
        Assert.Equal(Rgb.White, f.GetPixel(372, 204));
        Assert.Equal(Rgb.White, f.GetPixel(372 + 335, 204 + 671));
        Assert.Equal(Rgb.Black, f.GetPixel(371, 204));
        Assert.Equal(Rgb.Black, f.GetPixel(372, 203));
        Assert.Equal(Rgb.Black, f.GetPixel(372 + 336, 204));
        Assert.Equal(Rgb.Black, f.GetPixel(372, 204 + 672));
    }

    [Fact]
    public void Canvas_calibration_ignores_screens()
    {
        var withScreens = FrameComposer.Compose(OutputMode.OutputCanvasCalibration, 1920, 1080, new[] { S(1, 0, 0, 336, 672) });
        var without = FrameComposer.Compose(OutputMode.OutputCanvasCalibration, 1920, 1080, Array.Empty<CalibrationScreen>());
        Assert.Equal(without.Data, withScreens.Data);
        Assert.Equal(CanvasPattern.Render(1920, 1080).Data, without.Data);
    }

    [Fact]
    public void Screen_partly_outside_the_canvas_is_clipped_without_error()
    {
        var f = FrameComposer.Compose(OutputMode.ScreenCalibration, 400, 400, new[] { S(1, 300, 300, 336, 672) });
        Assert.Equal((400, 400), (f.Width, f.Height));
        Assert.True(NonBlack(f, 300, 300, 100, 100) > 0);
    }

    [Fact]
    public void Overlapping_screens_draw_in_list_order()
    {
        var a = S(1, 0, 0, 100, 100, "A");
        var b = S(2, 50, 50, 100, 100, "B");
        var f = FrameComposer.Compose(OutputMode.ScreenCalibration, 300, 300, new[] { a, b });
        // B's boundary pixel at its own top-left (50,50) is drawn over A
        Assert.Equal(Rgb.White, f.GetPixel(50, 50));
        Assert.Equal(ScreenPatterns.Calibration(b).GetPixel(10, 10), f.GetPixel(60, 60));
    }

    [Fact]
    public void Switching_back_to_normal_gives_black_again()
    {
        var screens = new[] { S(1, 0, 0, 336, 672) };
        Assert.True(NonBlack(FrameComposer.Compose(OutputMode.ScreenCalibration, 1920, 1080, screens)) > 0);
        Assert.Equal(0, NonBlack(FrameComposer.Compose(OutputMode.Normal, 1920, 1080, screens)));
    }
}

public class CalibrationPlanTests
{
    private static Screen S(string name, int x, int y, int w, int h, bool enabled = true) =>
        new() { Name = name, X = x, Y = y, Width = w, Height = h, Enabled = enabled };

    [Fact]
    public void Valid_enabled_screens_are_drawn_with_list_numbers()
    {
        var plan = CalibrationPlan.Build(new[] { S("Food", 0, 0, 336, 672), S("Drink", 336, 0, 336, 672) }, new CanvasSize(1920, 1080));
        Assert.Equal(new[] { 1, 2 }, plan.Drawn.Select(d => d.Number));
        Assert.Equal(new[] { "Food", "Drink" }, plan.Drawn.Select(d => d.Name));
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Invalid_screens_are_skipped_and_reported_never_drawn_as_if_valid()
    {
        var plan = CalibrationPlan.Build(new[]
        {
            S("Good", 0, 0, 336, 672),
            S("Off canvas", 1800, 0, 336, 672),
            S("Zero", 0, 0, 0, 10),
            S("Negative", -4, 0, 10, 10),
        }, new CanvasSize(1920, 1080));
        Assert.Equal(new[] { "Good" }, plan.Drawn.Select(d => d.Name));
        Assert.Equal(new[] { 2, 3, 4 }, plan.Skipped.Select(s => s.Number));
        Assert.All(plan.Skipped, s => Assert.False(string.IsNullOrWhiteSpace(s.Reason)));
    }

    [Fact]
    public void Disabled_screens_are_left_out_quietly()
    {
        var plan = CalibrationPlan.Build(new[] { S("Off", 0, 0, 100, 100, enabled: false), S("On", 200, 0, 100, 100) }, new CanvasSize(1920, 1080));
        Assert.Equal(new[] { "On" }, plan.Drawn.Select(d => d.Name));
        Assert.Empty(plan.Skipped);
        Assert.Equal(2, plan.Drawn[0].Number);   // numbering follows the list, not the drawn subset
    }

    [Fact]
    public void Overlapping_screens_are_both_drawn()
    {
        var plan = CalibrationPlan.Build(new[] { S("A", 0, 0, 200, 200), S("B", 100, 100, 200, 200) }, new CanvasSize(1920, 1080));
        Assert.Equal(2, plan.Drawn.Count);
    }

    [Fact]
    public void Unnamed_screens_get_a_default_label()
    {
        var plan = CalibrationPlan.Build(new[] { S("", 0, 0, 100, 100) }, new CanvasSize(1920, 1080));
        Assert.Equal("Screen 1", plan.Drawn.Single().Name);
    }
}
