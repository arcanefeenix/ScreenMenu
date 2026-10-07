namespace LedMenu.Core.Screens;

/// <summary>
/// A logical rectangular region inside the output canvas, normally one LED wall. It is not a Windows
/// monitor. It carries only geometry and an optional reference to the menu shown on it; menu content
/// lives elsewhere, so re-assigning a menu never changes the physical layout.
/// </summary>
public sealed class Screen
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Whether this screen shows a menu or a video playlist.</summary>
    public ScreenContentKind ContentKind { get; set; } = ScreenContentKind.Menu;

    /// <summary>Menu shown on this screen when <see cref="ContentKind"/> is Menu. Kept even while the screen shows video.</summary>
    public Guid? AssignedMenuId { get; set; }

    /// <summary>Videos shown on this screen when <see cref="ContentKind"/> is Video. Kept even while the screen shows a menu.</summary>
    public VideoPlaylist Playlist { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public bool ShowsVideo => ContentKind == ScreenContentKind.Video;

    public Screen Clone()
    {
        var copy = (Screen)MemberwiseClone();
        copy.Playlist = Playlist.Clone();
        return copy;
    }
}

/// <summary>Convenience sizes only. Nothing in the model or the validator depends on these.</summary>
public static class ScreenDefaults
{
    /// <summary>Two panels wide by two high at 168x336 per panel: the current expected LED wall.</summary>
    public const int Wall2x2Width = 336;
    public const int Wall2x2Height = 672;

    /// <summary>One panel wide by two high (168x336 per panel): a single narrow column of the wall.</summary>
    public const int Wall1x2Width = 168;
    public const int Wall1x2Height = 672;
}

public readonly record struct CanvasSize(int Width, int Height)
{
    public override string ToString() => $"{Width}×{Height}";
}
