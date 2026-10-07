namespace LedMenu.Core.Screens;

public enum IssueSeverity { Warning, Error }

public enum IssueCode
{
    EmptyName,
    DuplicateName,
    NonPositiveSize,
    NegativePosition,
    OutsideCanvas,
    Overlap,
    MissingMenu,
    EmptyPlaylist,
    MissingMedia,
    VideoSizeMismatch,
}

public sealed record ScreenIssue(Guid ScreenId, Guid? OtherScreenId, IssueCode Code, IssueSeverity Severity, string Message);

/// <summary>
/// Rules for screens against the output canvas. Validation only reports; it never changes a screen.
/// - Width and height must be positive; X and Y must not be negative (Error).
/// - A screen extending past the canvas is an Error when enabled (a Warning when disabled); it is never clipped.
/// - Overlap between two enabled screens is a Warning; overlap is allowed.
/// - Disabled screens take no part in overlap checks or rendering.
/// - A screen assigned to a menu that is not in the known set gets a warning (the assignment is never removed).
/// - If the canvas size is unknown (no output display selected) bounds cannot be checked.
/// - A video screen with nothing playable, a video whose file is gone, or a video whose size differs from the screen's gets a warning
///   (the entries are kept; the size warning is there because the picture will be fitted or cropped instead of shown 1:1).
/// </summary>
public static class ScreenValidator
{
    public static IReadOnlyList<ScreenIssue> Validate(
        IReadOnlyList<Screen> screens, CanvasSize? canvas, IReadOnlySet<Guid>? knownMenuIds = null,
        Func<string, bool>? mediaExists = null)
    {
        var issues = new List<ScreenIssue>();

        foreach (var s in screens)
        {
            if (s.ShowsVideo) ValidateVideo(s, mediaExists, issues);

            if (!s.ShowsVideo && knownMenuIds != null && s.AssignedMenuId is { } menuId && !knownMenuIds.Contains(menuId))
                issues.Add(new(s.Id, null, IssueCode.MissingMenu, IssueSeverity.Warning,
                    "The assigned menu no longer exists or could not be loaded. The assignment is kept; choose another menu or restore the file."));

            if (string.IsNullOrWhiteSpace(s.Name))
                issues.Add(new(s.Id, null, IssueCode.EmptyName, IssueSeverity.Warning, "This screen has no name."));

            var structurallyValid = true;
            if (s.Width <= 0 || s.Height <= 0)
            {
                structurallyValid = false;
                issues.Add(new(s.Id, null, IssueCode.NonPositiveSize, IssueSeverity.Error,
                    $"Width and height must be greater than zero (currently {s.Width}×{s.Height})."));
            }
            if (s.X < 0 || s.Y < 0)
            {
                structurallyValid = false;
                issues.Add(new(s.Id, null, IssueCode.NegativePosition, IssueSeverity.Error,
                    $"X and Y cannot be negative (currently {s.X}, {s.Y})."));
            }

            if (structurallyValid && canvas is { } c)
            {
                var right = (long)s.X + s.Width;
                var bottom = (long)s.Y + s.Height;
                if (right > c.Width || bottom > c.Height)
                {
                    var parts = new List<string>();
                    if (right > c.Width) parts.Add($"right edge {right} is past the canvas width {c.Width}");
                    if (bottom > c.Height) parts.Add($"bottom edge {bottom} is past the canvas height {c.Height}");
                    var sev = s.Enabled ? IssueSeverity.Error : IssueSeverity.Warning;
                    var tail = s.Enabled ? "" : " (it is disabled, but would be invalid if enabled)";
                    issues.Add(new(s.Id, null, IssueCode.OutsideCanvas, sev,
                        "Extends outside the output canvas: " + string.Join("; ", parts) + "." + tail));
                }
            }
        }

        foreach (var group in screens.Where(s => !string.IsNullOrWhiteSpace(s.Name))
                                     .GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                                     .Where(g => g.Count() > 1))
            foreach (var s in group)
                issues.Add(new(s.Id, null, IssueCode.DuplicateName, IssueSeverity.Warning,
                    $"Another screen is also named \"{group.Key}\"."));

        var live = screens.Where(s => s.Enabled && s.Width > 0 && s.Height > 0).ToList();
        for (var i = 0; i < live.Count; i++)
            for (var j = i + 1; j < live.Count; j++)
                if (TryIntersection(live[i], live[j], out var w, out var h))
                {
                    issues.Add(new(live[i].Id, live[j].Id, IssueCode.Overlap, IssueSeverity.Warning,
                        $"Overlaps \"{Label(live[j])}\" by {w}×{h} pixels."));
                    issues.Add(new(live[j].Id, live[i].Id, IssueCode.Overlap, IssueSeverity.Warning,
                        $"Overlaps \"{Label(live[i])}\" by {w}×{h} pixels."));
                }

        return issues;
    }

    private static void ValidateVideo(Screen s, Func<string, bool>? mediaExists, List<ScreenIssue> issues)
    {
        if (!s.Playlist.Playable.Any())
            issues.Add(new(s.Id, null, IssueCode.EmptyPlaylist, IssueSeverity.Warning,
                s.Playlist.Items.Count == 0 ? "This video screen has no videos yet; it stays black."
                                            : "Every video on this screen is switched off; it stays black."));

        foreach (var v in s.Playlist.Items)
        {
            var label = string.IsNullOrWhiteSpace(v.DisplayName) ? v.FileName : v.DisplayName;
            if (mediaExists != null && !mediaExists(v.FileName))
                issues.Add(new(s.Id, null, IssueCode.MissingMedia, IssueSeverity.Warning,
                    $"The video \"{label}\" is missing from the media folder; it will be skipped."));
            else if (v.Width > 0 && v.Height > 0 && s.Width > 0 && s.Height > 0 && (v.Width != s.Width || v.Height != s.Height))
                issues.Add(new(s.Id, null, IssueCode.VideoSizeMismatch, IssueSeverity.Warning,
                    $"The video \"{label}\" is {v.Width}×{v.Height} but this screen is {s.Width}×{s.Height}, so it will be {(s.Playlist.Fit == VideoFit.Fill ? "scaled and cropped" : "scaled to fit")} instead of shown 1:1."));
        }
    }

    /// <summary>Screens that are allowed to draw: enabled and free of errors.</summary>
    public static IReadOnlyList<Screen> RenderableScreens(IReadOnlyList<Screen> screens, CanvasSize? canvas)
    {
        var errors = Validate(screens, canvas)
            .Where(i => i.Severity == IssueSeverity.Error).Select(i => i.ScreenId).ToHashSet();
        return screens.Where(s => s.Enabled && !errors.Contains(s.Id)).ToList();
    }

    /// <summary>True when the rectangles share area. Rectangles that only touch along an edge do not overlap.</summary>
    public static bool Overlaps(Screen a, Screen b) => TryIntersection(a, b, out _, out _);

    private static bool TryIntersection(Screen a, Screen b, out long width, out long height)
    {
        var left = Math.Max((long)a.X, b.X);
        var top = Math.Max((long)a.Y, b.Y);
        var right = Math.Min((long)a.X + a.Width, (long)b.X + b.Width);
        var bottom = Math.Min((long)a.Y + a.Height, (long)b.Y + b.Height);
        width = right - left;
        height = bottom - top;
        return width > 0 && height > 0;
    }

    private static string Label(Screen s) => string.IsNullOrWhiteSpace(s.Name) ? "(unnamed)" : s.Name;
}
