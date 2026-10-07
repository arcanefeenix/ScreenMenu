using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LedMenu.Core.Calibration;

namespace LedMenu.App.Controls;

/// <summary>
/// Shows a rendered page at a whole-number zoom, with no smoothing, so each LED pixel is a crisp square.
/// At 1x on a screen with Windows scaling it covers exactly the page's pixel count in physical pixels.
/// </summary>
public sealed class PixelFrameControl : FrameworkElement
{
    public static readonly DependencyProperty FrameProperty = DependencyProperty.Register(
        nameof(Frame), typeof(PixelBuffer), typeof(PixelFrameControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            (d, _) => ((PixelFrameControl)d)._bitmap = null));

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(int), typeof(PixelFrameControl),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private BitmapSource? _bitmap;

    public PixelFrameControl()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
    }

    public PixelBuffer? Frame { get => (PixelBuffer?)GetValue(FrameProperty); set => SetValue(FrameProperty, value); }
    public int Zoom { get => (int)GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }

    private Size DesiredSizeInDips()
    {
        if (Frame is not { } f) return new Size(0, 0);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var zoom = Math.Max(1, Zoom);
        return new Size(f.Width * zoom / scale, f.Height * zoom / scale);
    }

    protected override Size MeasureOverride(Size availableSize) => DesiredSizeInDips();

    protected override void OnRender(DrawingContext dc)
    {
        if (Frame is not { } f) return;
        _bitmap ??= BitmapSource.Create(f.Width, f.Height, 96, 96, PixelFormats.Bgra32, null, f.Data, f.Width * 4);
        var size = DesiredSizeInDips();
        dc.DrawImage(_bitmap, new Rect(0, 0, size.Width, size.Height));
    }
}
