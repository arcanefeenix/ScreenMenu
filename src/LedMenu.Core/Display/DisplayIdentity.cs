namespace LedMenu.Core.Display;

/// <summary>
/// What we remember about a chosen display so it can be found again after a restart.
/// Plain mutable class so it serializes cleanly to JSON.
/// </summary>
public sealed class DisplayIdentity
{
    /// <summary>Stable monitor interface path; the primary matching key.</summary>
    public string DevicePath { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    /// <summary>Windows GDI name at the time of selection. Not stable; informational only.</summary>
    public string DeviceName { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public static DisplayIdentity From(DisplayInfo d) => new()
    {
        DevicePath = d.DevicePath,
        FriendlyName = d.FriendlyName,
        DeviceName = d.DeviceName,
        X = d.X,
        Y = d.Y,
        Width = d.Width,
        Height = d.Height,
    };

    /// <summary>True when the remembered geometry/name differs from <paramref name="d"/>.</summary>
    public bool DiffersFrom(DisplayInfo d) =>
        X != d.X || Y != d.Y || Width != d.Width || Height != d.Height ||
        FriendlyName != d.FriendlyName || DeviceName != d.DeviceName;

    public string Describe() => $"{FriendlyName} ({Width}×{Height})";
}
