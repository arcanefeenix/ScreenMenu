using LedMenu.Core.Calibration;

namespace LedMenu.Core.Tests;

public class PreviewMathTests
{
    [Theory]
    [InlineData(1920, 1080, 400, 560, 400.0 / 1920)]     // wide canvas: limited by width
    [InlineData(336, 672, 400, 560, 560.0 / 672)]        // tall screen: limited by height
    [InlineData(168, 672, 400, 560, 560.0 / 672)]
    [InlineData(100, 100, 400, 560, 4.0)]                // a 100x100 region fills the 400 wide box at 4x
    public void Fit_scale_is_the_largest_that_fits_the_box(int w, int h, double bw, double bh, double expected)
    {
        var s = PreviewMath.FitScale(w, h, bw, bh, maxScale: 8);
        Assert.Equal(Math.Min(8, expected), s, 6);
        Assert.True(w * s <= bw + 1e-9 && h * s <= bh + 1e-9);
    }

    [Fact]
    public void Fit_scale_is_capped_so_a_tiny_region_is_not_blown_up_absurdly() =>
        Assert.Equal(8, PreviewMath.FitScale(10, 10, 400, 560));

    [Theory]
    [InlineData(0, 100, 400, 560)]
    [InlineData(100, 0, 400, 560)]
    [InlineData(100, 100, 0, 560)]
    [InlineData(-5, 100, 400, -1)]
    public void Fit_scale_of_nonsense_is_one_not_a_crash(int w, int h, double bw, double bh) =>
        Assert.Equal(1, PreviewMath.FitScale(w, h, bw, bh));

    [Fact]
    public void A_screen_inside_the_canvas_is_its_own_region() =>
        Assert.Equal(new PixelRegion(372, 204, 336, 672), PreviewMath.ClampToCanvas(372, 204, 336, 672, 1920, 1080));

    [Fact]
    public void A_screen_hanging_off_the_canvas_is_clamped_so_nothing_outside_is_read()
    {
        Assert.Equal(new PixelRegion(1800, 0, 120, 672), PreviewMath.ClampToCanvas(1800, 0, 336, 672, 1920, 1080));
        Assert.Equal(new PixelRegion(0, 800, 336, 280), PreviewMath.ClampToCanvas(0, 800, 336, 672, 1920, 1080));
        var negative = PreviewMath.ClampToCanvas(-50, -50, 336, 672, 1920, 1080);
        Assert.Equal(new PixelRegion(0, 0, 286, 622), negative);          // only the part inside the canvas
    }

    [Fact]
    public void A_region_wholly_outside_or_empty_still_gives_a_valid_one_pixel_region_inside_the_canvas()
    {
        var r = PreviewMath.ClampToCanvas(5000, 5000, 100, 100, 1920, 1080);
        Assert.InRange(r.X, 0, 1919);
        Assert.InRange(r.Y, 0, 1079);
        Assert.True(r.Width >= 1 && r.Height >= 1);
        Assert.True(r.X + r.Width <= 1920 && r.Y + r.Height <= 1080);

        var z = PreviewMath.ClampToCanvas(10, 10, 0, 0, 1920, 1080);
        Assert.True(z.Width >= 1 && z.Height >= 1);
    }

    [Theory]
    [InlineData(1.0, "100% (every picture pixel is one screen pixel)")]
    [InlineData(0.5, "50% (scaled down to fit)")]
    [InlineData(0.2083, "21% (scaled down to fit)")]
    [InlineData(3.0, "300% (each LED pixel is a 3×3 block)")]
    [InlineData(2.5, "250% (enlarged)")]
    public void The_scale_is_described_in_plain_words(double scale, string expected) =>
        Assert.Equal(expected, PreviewMath.DescribeScale(scale));
}
