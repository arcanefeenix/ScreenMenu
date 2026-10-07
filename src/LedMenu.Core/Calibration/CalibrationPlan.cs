using LedMenu.Core.Screens;

namespace LedMenu.Core.Calibration;

public sealed record SkippedScreen(int Number, string Name, string Reason);

/// <summary>
/// Which screens a test pattern may be drawn on. Screens that are disabled are left out silently (they are off);
/// enabled screens with errors are NOT drawn and are reported, so an invalid screen is never shown as though valid.
/// Overlapping screens are drawn (overlap is allowed) in list order.
/// </summary>
public sealed record CalibrationPlan(IReadOnlyList<CalibrationScreen> Drawn, IReadOnlyList<SkippedScreen> Skipped)
{
    public static CalibrationPlan Build(IReadOnlyList<Screen> screens, CanvasSize? canvas)
    {
        var issues = ScreenValidator.Validate(screens, canvas);
        var drawn = new List<CalibrationScreen>();
        var skipped = new List<SkippedScreen>();

        for (var i = 0; i < screens.Count; i++)
        {
            var s = screens[i];
            if (!s.Enabled) continue;
            var firstError = issues.FirstOrDefault(x => x.ScreenId == s.Id && x.Severity == IssueSeverity.Error);
            var name = string.IsNullOrWhiteSpace(s.Name) ? $"Screen {i + 1}" : s.Name;
            if (firstError != null)
                skipped.Add(new SkippedScreen(i + 1, name, firstError.Message));
            else
                drawn.Add(new CalibrationScreen(s.Id, i + 1, name, s.X, s.Y, s.Width, s.Height, s.AssignedMenuId));
        }
        return new CalibrationPlan(drawn, skipped);
    }
}
