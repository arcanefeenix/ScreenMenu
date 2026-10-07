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

    /// <summary>Menu shown on this screen. Menus arrive in a later phase; the reference is preserved now.</summary>
    public Guid? AssignedMenuId { get; set; }

    public Screen Clone() => (Screen)MemberwiseClone();
}

/// <summary>Convenience sizes only. Nothing in the model or the validator depends on these.</summary>
public static class ScreenDefaults
{
    /// <summary>Two panels wide by two high at 168x336 per panel: the current expected LED wall.</summary>
    public const int Wall2x2Width = 336;
    public const int Wall2x2Height = 672;
}

public readonly record struct CanvasSize(int Width, int Height)
{
    public override string ToString() => $"{Width}×{Height}";
}
