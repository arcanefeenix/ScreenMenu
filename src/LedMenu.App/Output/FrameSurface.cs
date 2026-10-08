using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LedMenu.Core.Calibration;

namespace LedMenu.App.Output;

/// <summary>
/// Shows the output picture at exact 1:1 physical pixels. The picture lives in one long-lived bitmap: a whole new picture
/// (menus, test patterns) rewrites all of it, while a moving video only rewrites its own rectangle. Redrawing all of a
/// 1920x1080 picture 30 times a second just to move a 168x672 strip would be wasteful, and this keeps the cost proportional to the video.
/// The bitmap carries the monitor's own DPI, so its size in WPF units is exactly its size in pixels on that monitor.
/// </summary>
public sealed class FrameSurface : FrameworkElement
{
    private WriteableBitmap? _bitmap;
    private double _dpiX = 96, _dpiY = 96;

    public FrameSurface()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
    }

    /// <summary>Pixel size of what is shown, or 0x0 when the surface is pure black.</summary>
    public (int Width, int Height) PictureSize => _bitmap == null ? (0, 0) : (_bitmap.PixelWidth, _bitmap.PixelHeight);

    /// <summary>Shows the whole picture (null = pure black).</summary>
    public void Show(PixelBuffer? frame, double dpiX, double dpiY)
    {
        if (frame == null) { Clear(); return; }

        if (_bitmap == null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height ||
            Math.Abs(_dpiX - dpiX) > 0.01 || Math.Abs(_dpiY - dpiY) > 0.01)
        {
            _dpiX = dpiX; _dpiY = dpiY;
            _bitmap = new WriteableBitmap(frame.Width, frame.Height, dpiX, dpiY, PixelFormats.Bgra32, null);
            InvalidateMeasure();
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Data, frame.Width * 4, 0);
        InvalidateVisual();
    }

    /// <summary>
    /// Refreshes only <paramref name="region"/> from <paramref name="frame"/>, which must be the same picture that is showing
    /// (same size). If it is not, the whole picture is shown instead, so a mismatch can never leave stale or torn content.
    /// </summary>
    public void Update(PixelBuffer frame, PixelRegion region)
    {
        if (_bitmap == null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
        {
            Show(frame, _dpiX, _dpiY);
            return;
        }

        var x = Math.Max(0, region.X);
        var y = Math.Max(0, region.Y);
        var right = Math.Min(frame.Width, region.X + region.Width);
        var bottom = Math.Min(frame.Height, region.Y + region.Height);
        if (right <= x || bottom <= y) return;

        var rect = new Int32Rect(x, y, right - x, bottom - y);
        _bitmap.WritePixels(rect, frame.Data, frame.Width * 4, x, y);
    }

    public void Clear()
    {
        if (_bitmap == null) return;
        _bitmap = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        _bitmap == null ? new Size(0, 0) : new Size(_bitmap.Width, _bitmap.Height);

    protected override void OnRender(DrawingContext dc)
    {
        if (_bitmap != null) dc.DrawImage(_bitmap, new Rect(0, 0, _bitmap.Width, _bitmap.Height));
    }
}
