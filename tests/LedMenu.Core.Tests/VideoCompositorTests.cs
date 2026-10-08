using LedMenu.Core.Calibration;
using LedMenu.Core.Screens;
using LedMenu.Core.Video;

namespace LedMenu.Core.Tests;

public class RegionMathTests
{
    private static PixelRegion R(int x, int y, int w, int h) => new(x, y, w, h);

    private static HashSet<(int, int)> Cells(IEnumerable<PixelRegion> rs)
    {
        var set = new HashSet<(int, int)>();
        foreach (var r in rs) for (var y = r.Y; y < r.Y + r.Height; y++) for (var x = r.X; x < r.X + r.Width; x++) Assert.True(set.Add((x, y)), $"pixel {x},{y} appears twice");
        return set;
    }

    [Fact]
    public void Intersect_finds_the_shared_rectangle_or_nothing()
    {
        Assert.Equal(R(5, 5, 5, 5), RegionMath.Intersect(R(0, 0, 10, 10), R(5, 5, 10, 10)));
        Assert.Null(RegionMath.Intersect(R(0, 0, 10, 10), R(10, 0, 5, 5)));      // touching edges share no pixels
        Assert.Null(RegionMath.Intersect(R(0, 0, 10, 10), R(50, 50, 5, 5)));
        Assert.Equal(R(2, 2, 3, 3), RegionMath.Intersect(R(0, 0, 10, 10), R(2, 2, 3, 3)));
    }

    [Fact]
    public void Subtracting_something_that_does_not_touch_leaves_the_region_whole() =>
        Assert.Equal(new[] { R(0, 0, 10, 10) }, RegionMath.Subtract(R(0, 0, 10, 10), R(20, 20, 5, 5)));

    [Fact]
    public void Subtracting_everything_leaves_nothing() =>
        Assert.Empty(RegionMath.Subtract(R(2, 2, 4, 4), R(0, 0, 10, 10)));

    [Fact]
    public void Cutting_out_the_middle_leaves_four_pieces_that_tile_the_rest_exactly()
    {
        var parts = RegionMath.Subtract(R(0, 0, 10, 10), R(3, 3, 4, 4));
        Assert.Equal(4, parts.Count);
        Assert.Equal(100 - 16, Cells(parts).Count);
        Assert.DoesNotContain((4, 4), Cells(parts));
    }

    [Fact]
    public void Cutting_a_corner_or_an_edge_leaves_two_or_three_pieces()
    {
        Assert.Equal(2, RegionMath.Subtract(R(0, 0, 10, 10), R(5, 5, 20, 20)).Count);
        Assert.Equal(3, RegionMath.Subtract(R(0, 0, 10, 10), R(0, 4, 3, 2)).Count);
    }

    [Fact]
    public void Visible_parts_after_several_covers_match_a_pixel_by_pixel_check_on_random_layouts()
    {
        var rnd = new Random(7);
        for (var round = 0; round < 300; round++)
        {
            var region = R(rnd.Next(0, 20), rnd.Next(0, 20), rnd.Next(1, 30), rnd.Next(1, 30));
            var covers = Enumerable.Range(0, rnd.Next(0, 5)).Select(_ => R(rnd.Next(-5, 40), rnd.Next(-5, 40), rnd.Next(1, 25), rnd.Next(1, 25))).ToList();

            var expected = new HashSet<(int, int)>();
            for (var y = region.Y; y < region.Y + region.Height; y++)
                for (var x = region.X; x < region.X + region.Width; x++)
                    if (!covers.Any(c => x >= c.X && x < c.X + c.Width && y >= c.Y && y < c.Y + c.Height)) expected.Add((x, y));

            var actual = Cells(RegionMath.VisibleParts(region, covers));     // also asserts no pixel is listed twice
            Assert.True(expected.SetEquals(actual), $"round {round}: region {region}, covers {string.Join(" ", covers)}");
        }
    }

    [Fact]
    public void An_empty_region_has_nothing_visible() => Assert.Empty(RegionMath.VisibleParts(R(0, 0, 0, 5), Array.Empty<PixelRegion>()));
}

public class VideoCompositorTests
{
    private static readonly CanvasSize Canvas = new(100, 60);

    private static CalibrationScreen Menu(string name, int x, int y, int w, int h) => new(Guid.NewGuid(), 1, name, x, y, w, h, Guid.NewGuid(), false);
    private static CalibrationScreen Video(string name, int x, int y, int w, int h) => new(Guid.NewGuid(), 1, name, x, y, w, h, null, true);

    private static PixelBuffer Solid(int w, int h, byte r, byte g, byte b)
    {
        var p = new PixelBuffer(w, h);
        for (var i = 0; i < p.Data.Length; i += 4) { p.Data[i] = b; p.Data[i + 1] = g; p.Data[i + 2] = r; }
        return p;
    }

    private static (byte R, byte G, byte B) At(PixelBuffer p, int x, int y)
    {
        var i = (y * p.Width + x) * 4;
        return (p.Data[i + 2], p.Data[i + 1], p.Data[i]);
    }

    private static readonly (byte, byte, byte) Black = (0, 0, 0);

    [Fact]
    public void A_video_lands_exactly_in_its_rectangle_and_nowhere_else()
    {
        var v = Video("v", 20, 10, 30, 40);
        var canvas = new PixelBuffer(Canvas.Width, Canvas.Height);
        var written = VideoCompositor.Blit(canvas, v, Solid(30, 40, 255, 0, 0), new[] { v });

        Assert.Equal(new[] { new PixelRegion(20, 10, 30, 40) }, written);
        for (var y = 0; y < Canvas.Height; y++)
            for (var x = 0; x < Canvas.Width; x++)
            {
                var inside = x >= 20 && x < 50 && y >= 10 && y < 50;
                Assert.Equal(inside ? ((byte)255, (byte)0, (byte)0) : Black, At(canvas, x, y));
            }
    }

    [Fact]
    public void Every_pixel_of_the_video_goes_to_the_matching_pixel_of_the_screen()
    {
        var v = Video("v", 7, 3, 5, 4);
        var pic = new PixelBuffer(5, 4);
        for (var y = 0; y < 4; y++) for (var x = 0; x < 5; x++) { var i = (y * 5 + x) * 4; pic.Data[i + 2] = (byte)(10 * x + 1); pic.Data[i + 1] = (byte)(10 * y + 1); }
        var canvas = new PixelBuffer(Canvas.Width, Canvas.Height);
        VideoCompositor.Blit(canvas, v, pic, new[] { v });
        for (var y = 0; y < 4; y++) for (var x = 0; x < 5; x++)
            Assert.Equal(((byte)(10 * x + 1), (byte)(10 * y + 1), (byte)0), At(canvas, 7 + x, 3 + y));
    }

    [Fact]
    public void A_neighbouring_menu_screen_is_left_completely_untouched()
    {
        var menu = Menu("menu", 0, 0, 40, 60);
        var v = Video("v", 40, 0, 40, 60);
        var canvas = Solid(Canvas.Width, Canvas.Height, 1, 2, 3);                 // pretend the menu picture is already there
        var before = (byte[])canvas.Data.Clone();

        VideoCompositor.Blit(canvas, v, Solid(40, 60, 200, 200, 200), new[] { menu, v });

        for (var y = 0; y < 60; y++)
            for (var x = 0; x < 100; x++)
            {
                var i = (y * 100 + x) * 4;
                if (x >= 40 && x < 80) Assert.Equal(200, canvas.Data[i]);
                else Assert.Equal(before[i], canvas.Data[i]);                     // outside the video screen, not one byte changed
            }
    }

    [Fact]
    public void A_screen_drawn_after_the_video_stays_on_top_where_they_overlap()
    {
        var v = Video("v", 0, 0, 50, 50);
        var over = Menu("over", 30, 30, 40, 40);                                  // later in the list = on top
        var canvas = Solid(Canvas.Width, Canvas.Height, 9, 9, 9);
        var written = VideoCompositor.Blit(canvas, v, Solid(50, 50, 255, 0, 0), new[] { v, over });

        Assert.Equal((255, 0, 0), ((int)At(canvas, 10, 10).R, (int)At(canvas, 10, 10).G, (int)At(canvas, 10, 10).B));
        Assert.Equal(((byte)9, (byte)9, (byte)9), At(canvas, 35, 35));            // the covered corner still shows the screen on top
        Assert.Equal(((byte)9, (byte)9, (byte)9), At(canvas, 49, 49));
        Assert.Equal(50 * 50 - 20 * 20, written.Sum(r => r.Width * r.Height));
    }

    [Fact]
    public void A_screen_drawn_before_the_video_is_covered_by_it()
    {
        var under = Menu("under", 0, 0, 50, 50);
        var v = Video("v", 25, 25, 50, 30);
        var canvas = Solid(Canvas.Width, Canvas.Height, 9, 9, 9);
        VideoCompositor.Blit(canvas, v, Solid(50, 30, 255, 0, 0), new[] { under, v });
        Assert.Equal((byte)255, At(canvas, 30, 30).R);                            // overlap belongs to the later (video) screen
    }

    [Fact]
    public void A_picture_of_the_wrong_size_is_refused_and_writes_nothing()
    {
        var v = Video("v", 10, 10, 30, 30);
        var canvas = new PixelBuffer(Canvas.Width, Canvas.Height);
        Assert.Empty(VideoCompositor.Blit(canvas, v, Solid(31, 30, 255, 255, 255), new[] { v }));
        Assert.Empty(VideoCompositor.Blit(canvas, v, Solid(30, 29, 255, 255, 255), new[] { v }));
        Assert.All(canvas.Data.Where((_, i) => i % 4 != 3), b => Assert.Equal(0, b));
    }

    [Fact]
    public void A_screen_that_hangs_off_the_canvas_is_clipped_to_the_canvas_not_written_past_its_end()
    {
        var v = Video("v", 80, 40, 40, 40);                                       // 20 pixels wider and 20 taller than the canvas allows
        var canvas = new PixelBuffer(Canvas.Width, Canvas.Height);
        var written = VideoCompositor.Blit(canvas, v, Solid(40, 40, 255, 0, 0), new[] { v });
        Assert.Equal(new[] { new PixelRegion(80, 40, 20, 20) }, written);
        Assert.Equal((byte)255, At(canvas, 99, 59).R);
        Assert.Equal((byte)0, At(canvas, 79, 59).R);
    }

    [Fact]
    public void Compose_with_no_video_screens_returns_the_menu_picture_untouched()
    {
        var menu = Solid(Canvas.Width, Canvas.Height, 5, 5, 5);
        var result = VideoCompositor.Compose(menu, Canvas, new[] { Menu("m", 0, 0, 10, 10) }, _ => null);
        Assert.Same(menu, result);
        Assert.Null(VideoCompositor.Compose(null, Canvas, new[] { Menu("m", 0, 0, 10, 10) }, _ => null));
    }

    [Fact]
    public void Compose_with_a_video_and_no_menu_picture_gives_a_black_canvas_with_the_video_in_it()
    {
        var v = Video("v", 10, 10, 20, 20);
        var result = VideoCompositor.Compose(null, Canvas, new[] { v }, id => id == v.Id ? Solid(20, 20, 0, 255, 0) : null)!;
        Assert.Equal((Canvas.Width, Canvas.Height), (result.Width, result.Height));
        Assert.Equal(((byte)0, (byte)255, (byte)0), At(result, 15, 15));
        Assert.Equal(Black, At(result, 5, 5));
    }

    [Fact]
    public void Compose_leaves_a_video_with_no_picture_yet_black_and_keeps_the_menus()
    {
        var m = Menu("m", 0, 0, 30, 30);
        var v = Video("v", 50, 0, 30, 30);
        var menuPicture = Solid(Canvas.Width, Canvas.Height, 7, 7, 7);
        var result = VideoCompositor.Compose(menuPicture, Canvas, new[] { m, v }, _ => null)!;
        Assert.Same(menuPicture, result);
        Assert.Equal((byte)7, At(result, 10, 10).R);
    }

    [Fact]
    public void Compose_puts_every_video_screen_in_and_respects_overlap_order()
    {
        var a = Video("a", 0, 0, 40, 40);
        var b = Video("b", 20, 20, 40, 40);                                       // later: on top of a
        var result = VideoCompositor.Compose(null, Canvas, new[] { a, b },
            id => id == a.Id ? Solid(40, 40, 255, 0, 0) : Solid(40, 40, 0, 0, 255))!;
        Assert.Equal((byte)255, At(result, 5, 5).R);
        Assert.Equal((byte)255, At(result, 30, 30).B);                            // overlap shows b
        Assert.Equal((byte)0, At(result, 30, 30).R);
    }

    [Fact]
    public void A_menu_picture_of_the_wrong_size_is_not_trusted_a_fresh_canvas_is_used()
    {
        var v = Video("v", 0, 0, 10, 10);
        var wrong = new PixelBuffer(50, 50);
        var result = VideoCompositor.Compose(wrong, Canvas, new[] { v }, _ => Solid(10, 10, 1, 1, 1))!;
        Assert.NotSame(wrong, result);
        Assert.Equal((Canvas.Width, Canvas.Height), (result.Width, result.Height));
    }
}
