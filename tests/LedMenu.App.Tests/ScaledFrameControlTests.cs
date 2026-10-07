using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LedMenu.App.Controls;
using LedMenu.Core.Calibration;

namespace LedMenu.App.Tests;

public class ScaledFrameControlTests
{
    private static void Sta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    /// <summary>A frame where every pixel is unique-ish, so any shift, blur or scaling shows up as a mismatch.</summary>
    private static PixelBuffer Pattern(int w, int h)
    {
        var f = new PixelBuffer(w, h);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                f.Data[i] = (byte)(x * 7 + y);        // B
                f.Data[i + 1] = (byte)(y * 5 + x * 3);  // G
                f.Data[i + 2] = (byte)(x ^ y);          // R
                f.Data[i + 3] = 255;
            }
        return f;
    }

    private static (byte[] pixels, int w, int h) Render(PixelBuffer frame, PixelRegion region, double scale)
    {
        var c = new ScaledFrameControl { Frame = frame, Region = region, Scale = scale };
        c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        c.Arrange(new Rect(c.DesiredSize));
        c.UpdateLayout();
        // render at the control's own DPI so the bitmap is in physical pixels, as on the real display at any Windows scaling
        var dpi = VisualTreeHelper.GetDpi(c);
        var w = (int)Math.Round(c.DesiredSize.Width * dpi.DpiScaleX);
        var h = (int)Math.Round(c.DesiredSize.Height * dpi.DpiScaleY);
        var rtb = new RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(c);
        var px = new byte[w * h * 4];
        rtb.CopyPixels(px, w * 4, 0);
        return (px, w, h);
    }

    [Fact]
    public void At_100_percent_a_screen_region_is_pixel_identical_to_that_part_of_the_frame() => Sta(() =>
    {
        var frame = Pattern(1920, 1080);
        var region = new PixelRegion(37, 51, 336, 672);
        var (px, w, h) = Render(frame, region, 1);
        Assert.Equal((336, 672), (w, h));
        var bad = 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var s = ((region.Y + y) * frame.Width + region.X + x) * 4;
                var d = (y * w + x) * 4;
                if (px[d] != frame.Data[s] || px[d + 1] != frame.Data[s + 1] || px[d + 2] != frame.Data[s + 2]) bad++;
            }
        Assert.Equal(0, bad);
    });

    [Fact]
    public void At_300_percent_every_picture_pixel_becomes_an_exact_3_by_3_block() => Sta(() =>
    {
        var frame = Pattern(200, 200);
        var region = new PixelRegion(10, 20, 24, 16);
        var (px, w, h) = Render(frame, region, 3);
        Assert.Equal((72, 48), (w, h));
        var bad = 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var s = ((region.Y + y / 3) * frame.Width + region.X + x / 3) * 4;
                var d = (y * w + x) * 4;
                if (px[d] != frame.Data[s] || px[d + 1] != frame.Data[s + 1] || px[d + 2] != frame.Data[s + 2]) bad++;
            }
        Assert.Equal(0, bad);
    });

    [Fact]
    public void Fit_makes_the_whole_output_fit_the_preview_box() => Sta(() =>
    {
        var dpi = Math.Max(1, System.Windows.Media.VisualTreeHelper.GetDpi(new ScaledFrameControl()).DpiScaleX);
        var (_, w, h) = Render(Pattern(1920, 1080), new PixelRegion(0, 0, 1920, 1080), 0);
        Assert.InRange(w, 400 * dpi - 1, 400 * dpi + 1);     // the preview box is 400 DIPs wide
        Assert.True(h < 560 * dpi);
    });
}
