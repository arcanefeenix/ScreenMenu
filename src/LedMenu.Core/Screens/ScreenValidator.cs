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
/// </summary>
public static class ScreenValidator
{
    public static IReadOnlyList<ScreenIssue> Validate(
        IReadOnlyList<Screen> screens, CanvasSize? canvas, IReadOnlySet<Guid>? knownMenuIds = null)
    {
        var issues = new List<ScreenIssue>();

        foreach (var s in screens)
        {
            if (knownMenuIds != null && s.AssignedMenuId is { } menuId && !knownMenuIds.Contains(menuId))
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
