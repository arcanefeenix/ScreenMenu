namespace LedMenu.Core.Screens;

/// <summary>
/// A named set of screens. This is the unit that can later be saved as a preset
/// ("Single 2x2 Wall", "Dual 2x2 Walls"). It does not contain menus or the selected Windows display.
/// </summary>
public sealed class ScreenLayout
{
    /// <summary>1: screens with a menu. 2: screens may show a video playlist instead (ContentKind, Playlist).</summary>
    public const int CurrentSchemaVersion = 2;

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

        foreach (var s in layout.Screens)
        {
            if (!Enum.IsDefined(s.ContentKind)) return $"Screen \"{s.Name}\" has an unknown content type.";
            if (s.Playlist is null) return $"Screen \"{s.Name}\" has no playlist object.";
            if (s.Playlist.Items is null || s.Playlist.Items.Any(i => i is null)) return $"Screen \"{s.Name}\" has a damaged playlist.";
            if (!Enum.IsDefined(s.Playlist.Fit)) return $"Screen \"{s.Name}\" has an unknown video fit setting.";
            var bad = s.Playlist.Items.FirstOrDefault(i => !Assets.MediaRules.IsSafeFileName(i.FileName));
            if (bad != null) return $"Screen \"{s.Name}\" has a video with an unsafe file name.";
            var dupVideo = s.Playlist.Items.GroupBy(i => i.Id).FirstOrDefault(g => g.Count() > 1);
            if (dupVideo != null) return $"Screen \"{s.Name}\" lists a video twice with the same id.";
        }
        return null;

    }

    /// <summary>Brings a layout read from an older file up to the current schema. Old files lack the new fields, which already have their defaults.</summary>
    public static void Upgrade(ScreenLayout layout)
    {
        foreach (var s in layout.Screens)
        {
            s.Playlist ??= new VideoPlaylist();
            s.Playlist.Items ??= new List<VideoItem>();
        }
        layout.SchemaVersion = CurrentSchemaVersion;
    }
}
