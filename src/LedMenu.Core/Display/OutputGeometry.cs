namespace LedMenu.Core.Display;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public override string ToString() => $"({X},{Y}) {Width}×{Height}";
}

/// <summary>What Windows and WPF report about the output window, compared with what the display demands.</summary>
public sealed record OutputGeometryReport(
    PixelRect ExpectedBounds,
    PixelRect WindowRect,
    int ClientWidth,
    int ClientHeight,
    double WpfDpiX,
    double WpfDpiY,
    int MonitorDpiX,
    int MonitorDpiY,
    IReadOnlyList<string> Problems)
{
    public bool Agrees => Problems.Count == 0;
}

public static class OutputGeometry
{
    /// <summary>Converts physical pixels to WPF device-independent units for a given DPI.</summary>
    public static double ToDips(int pixels, double dpi) => pixels * 96.0 / dpi;

    /// <summary>Converts WPF units to physical pixels for a given DPI (rounded to the nearest pixel).</summary>
    public static int ToPixels(double dips, double dpi) => (int)Math.Round(dips * dpi / 96.0);

    public static OutputGeometryReport Evaluate(
        PixelRect expected, PixelRect window, int clientWidth, int clientHeight,
        double wpfDpiX, double wpfDpiY, int monitorDpiX, int monitorDpiY)
    {
        var problems = new List<string>();

        if (window != expected)
            problems.Add($"Window rectangle {window} does not equal the display bounds {expected}.");
        if (clientWidth != expected.Width || clientHeight != expected.Height)
            problems.Add($"Client area is {clientWidth}×{clientHeight} but the display is {expected.Width}×{expected.Height}.");
        if (Math.Abs(wpfDpiX - monitorDpiX) > 0.5 || Math.Abs(wpfDpiY - monitorDpiY) > 0.5)
            problems.Add($"WPF reports {wpfDpiX:0.#}/{wpfDpiY:0.#} DPI but the monitor reports {monitorDpiX}/{monitorDpiY}.");

        return new OutputGeometryReport(expected, window, clientWidth, clientHeight,
            wpfDpiX, wpfDpiY, monitorDpiX, monitorDpiY, problems);
    }
}
