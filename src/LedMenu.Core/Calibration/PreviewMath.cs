namespace LedMenu.Core.Calibration;

/// <summary>A rectangle of whole pixels inside a picture.</summary>
public readonly record struct PixelRegion(int X, int Y, int Width, int Height)
{
    public override string ToString() => $"({X},{Y}) {Width}×{Height}";
}

/// <summary>Arithmetic for showing the output picture in the operator window. Nothing here touches the picture itself.</summary>
public static class PreviewMath
{
    /// <summary>The largest scale at which a region of the given size fits inside a box, never above <paramref name="maxScale"/>.</summary>
    public static double FitScale(int width, int height, double boxWidth, double boxHeight, double maxScale = 8)
    {
        if (width <= 0 || height <= 0 || boxWidth <= 0 || boxHeight <= 0) return 1;
        return Math.Min(maxScale, Math.Min(boxWidth / width, boxHeight / height));
    }

    /// <summary>The part of the canvas a screen occupies, clamped to the canvas so a bad screen can never read outside it.</summary>
    public static PixelRegion ClampToCanvas(int x, int y, int width, int height, int canvasWidth, int canvasHeight)
    {
        var x0 = Math.Clamp(x, 0, Math.Max(0, canvasWidth - 1));
        var y0 = Math.Clamp(y, 0, Math.Max(0, canvasHeight - 1));
        var x1 = Math.Clamp((long)x + width, x0 + 1, canvasWidth);
        var y1 = Math.Clamp((long)y + height, y0 + 1, canvasHeight);
        return new PixelRegion(x0, y0, (int)(x1 - x0), (int)(y1 - y0));
    }

    /// <summary>"100% (actual pixels)", "40% (scaled down)", "300% (each LED pixel is a 3x3 block)".</summary>
    public static string DescribeScale(double scale)
    {
        var pct = (int)Math.Round(scale * 100);
        if (Math.Abs(scale - 1) < 0.005) return "100% (every picture pixel is one screen pixel)";
        if (scale < 1) return $"{pct}% (scaled down to fit)";
        var whole = Math.Abs(scale - Math.Round(scale)) < 0.005;
        return whole ? $"{pct}% (each LED pixel is a {(int)Math.Round(scale)}×{(int)Math.Round(scale)} block)" : $"{pct}% (enlarged)";
    }
}
