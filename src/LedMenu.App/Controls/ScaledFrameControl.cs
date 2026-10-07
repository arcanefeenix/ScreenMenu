using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LedMenu.App.ViewModels;
using LedMenu.Core.Calibration;

namespace LedMenu.App.Controls;

/// <summary>
/// Draws part of an output picture at a chosen scale. At 100% one picture pixel is exactly one physical screen pixel
/// (whatever the Windows scaling), enlargements are whole-number blocks with no smoothing, and "fit" scales down with
/// smoothing. The picture is the very one sent to the LED output; this control only displays it.
/// </summary>
public sealed class ScaledFrameControl : FrameworkElement
{
    public static readonly DependencyProperty FrameProperty = DependencyProperty.Register(
        nameof(Frame), typeof(PixelBuffer), typeof(ScaledFrameControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            (d, _) => ((ScaledFrameControl)d).Invalidate()));

    public static readonly DependencyProperty RegionProperty = DependencyProperty.Register(
        nameof(Region), typeof(PixelRegion), typeof(ScaledFrameControl),
        new FrameworkPropertyMetadata(default(PixelRegion), FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            (d, _) => ((ScaledFrameControl)d)._crop = null));

    /// <summary>Picture pixels to screen pixels: 1 is actual pixels; 0 means fit inside the preview box.</summary>
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(
        nameof(Scale), typeof(double), typeof(ScaledFrameControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private BitmapSource? _bitmap;
    private BitmapSource? _crop;

    public ScaledFrameControl()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    public PixelBuffer? Frame { get => (PixelBuffer?)GetValue(FrameProperty); set => SetValue(FrameProperty, value); }
    public PixelRegion Region { get => (PixelRegion)GetValue(RegionProperty); set => SetValue(RegionProperty, value); }
    public double Scale { get => (double)GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }

    private void Invalidate() { _bitmap = null; _crop = null; }

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    /// <summary>The region actually drawn: the requested one limited to the picture.</summary>
    private PixelRegion Effective()
    {
        var f = Frame;
        if (f == null) return default;
        var r = Region;
        if (r.Width <= 0 || r.Height <= 0) return new PixelRegion(0, 0, f.Width, f.Height);
        return PreviewMath.ClampToCanvas(r.X, r.Y, r.Width, r.Height, f.Width, f.Height);
    }

    /// <summary>Screen pixels per picture pixel actually used.</summary>
    private double PhysicalScale(PixelRegion r) =>
        Scale > 0 ? Scale : PreviewMath.FitScale(r.Width, r.Height, PreviewViewModel.BoxWidth * Dpi(), PreviewViewModel.BoxHeight * Dpi());

    private double Dpi() => VisualTreeHelper.GetDpi(this).DpiScaleX;

    protected override Size MeasureOverride(Size availableSize)
    {
        var r = Effective();
        if (r.Width <= 0) return new Size(0, 0);
        var s = PhysicalScale(r) / Dpi();
        return new Size(r.Width * s, r.Height * s);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var f = Frame;
        var r = Effective();
        if (f == null || r.Width <= 0) return;

        _bitmap ??= BitmapSource.Create(f.Width, f.Height, 96, 96, PixelFormats.Bgra32, null, f.Data, f.Width * 4);
        _crop ??= r.X == 0 && r.Y == 0 && r.Width == f.Width && r.Height == f.Height
            ? _bitmap
            : new CroppedBitmap(_bitmap, new Int32Rect(r.X, r.Y, r.Width, r.Height));

        var physical = PhysicalScale(r);
        RenderOptions.SetBitmapScalingMode(this, physical >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        var size = MeasureOverride(default);
        dc.DrawImage(_crop, new Rect(0, 0, size.Width, size.Height));
    }
}
