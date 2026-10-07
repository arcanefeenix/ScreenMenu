namespace LedMenu.Core.Display;

/// <summary>
/// One connected Windows display as seen right now. Windows numbering (\\.\DISPLAY1) is
/// recorded for diagnostics only; <see cref="DevicePath"/> is the identity used for matching.
/// Bounds are in physical pixels (the app is PerMonitorV2 DPI aware).
/// </summary>
public sealed record DisplayInfo(
    string DevicePath,
    string DeviceName,
    string FriendlyName,
    int X,
    int Y,
    int Width,
    int Height,
    int DpiX,
    int DpiY,
    bool IsPrimary,
    long Handle)
{
    public int ScalePercent => (int)Math.Round(DpiX * 100.0 / 96.0);
    public string Resolution => $"{Width}×{Height}";
}
