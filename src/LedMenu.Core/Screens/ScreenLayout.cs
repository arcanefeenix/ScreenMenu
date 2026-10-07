namespace LedMenu.Core.Screens;

/// <summary>
/// A named set of screens. This is the unit that can later be saved as a preset
/// ("Single 2x2 Wall", "Dual 2x2 Walls"). It does not contain menus or the selected Windows display.
/// </summary>
public sealed class ScreenLayout
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Name { get; set; } = "Default";
    public List<Screen> Screens { get; set; } = new();

    /// <summary>
    /// Structural check used when reading or writing the file. Geometry problems (outside the canvas,
    /// overlap, even negative values) are deliberately NOT rejected here: a screen definition is user data
    /// that must be preserved and flagged, never discarded or silently changed.
    /// </summary>
    public static string? ValidateFile(ScreenLayout? layout)
    {
        if (layout is null) return "Screen layout file is empty.";
        if (layout.SchemaVersion < 1) return $"Invalid schema version {layout.SchemaVersion}.";
        if (layout.SchemaVersion > CurrentSchemaVersion)
            return $"Screen layout was written by a newer version (schema {layout.SchemaVersion}).";
        if (layout.Screens is null) return "Screen list is missing.";
        if (layout.Screens.Any(s => s is null)) return "Screen list contains an empty entry.";
        var dup = layout.Screens.GroupBy(s => s.Id).FirstOrDefault(g => g.Count() > 1);
        if (dup != null) return $"Two screens share the id {dup.Key}.";
        return null;
    }
}
