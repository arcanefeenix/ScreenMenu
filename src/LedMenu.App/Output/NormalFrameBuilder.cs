using LedMenu.Core.Calibration;
using LedMenu.Core.Layout;
using LedMenu.Core.Menus;
using LedMenu.Rendering;
using LedMenu.Core.Screens;

namespace LedMenu.App.Output;

/// <summary>What <see cref="NormalFrameBuilder.Build"/> produced.</summary>
public sealed record NormalFrameResult(
    PixelBuffer? Frame,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<Guid, (Guid MenuId, int Page, int Count)> Shown);

/// <summary>
/// Builds the "Normal Output" picture: a black canvas with each assigned menu's current page placed at its screen.
/// This is the one place that decides it, for the LED output and for the operator's preview alike.
/// Screens with no menu, a missing menu, or errors stay black. Every page is the screen's own pixel size.
/// A menu that throws while drawing blacks out its own screen only and is reported as a warning.
/// </summary>
public sealed class NormalFrameBuilder
{
    private readonly Func<Guid, Menu?> _findMenu;
    private readonly IMenuRenderer _render;
    private readonly PageClock _clock;
    private readonly Func<TimeSpan> _now;

    public NormalFrameBuilder(Func<Guid, Menu?> findMenu, IMenuRenderer render, PageClock clock, Func<TimeSpan> now)
    {
        _findMenu = findMenu;
        _render = render;
        _clock = clock;
        _now = now;
    }

    /// <summary>The frame is null when nothing at all is drawn (the whole canvas is pure black).</summary>
    public NormalFrameResult Build(CanvasSize canvas, CalibrationPlan plan)
    {
        var parts = new List<(int, int, PixelBuffer)>();
        var warnings = new List<string>();
        var shown = new Dictionary<Guid, (Guid, int, int)>();

        foreach (var s in plan.Drawn)
        {
            if (s.MenuId is not { } menuId || _findMenu(menuId) is not { } menu) continue;

            MenuRenderResult result;
            try
            {
                result = _render.Get(menu, s.Width, s.Height);
            }
            catch (Exception ex)
            {
                // one menu that cannot be drawn blacks out only its own screen; every other screen carries on
                warnings.Add($"Screen {s.Number} \"{s.Name}\" is black because menu \"{menu.Name}\" could not be drawn: {ex.Message}");
                continue;
            }
            foreach (var p in result.Problems) warnings.Add($"{menu.Name}: {p.Message}");

            var page = _clock.PageFor(s.Id, result.PageCount, PageClock.Period(menu.Theme.PageSeconds), _now());
            shown[s.Id] = (menuId, page, result.PageCount);
            parts.Add((s.X, s.Y, result.Pages[Math.Min(page, result.PageCount - 1)]));
        }

        var frame = parts.Count == 0 ? null : FrameComposer.ComposeScreens(canvas.Width, canvas.Height, parts);
        return new NormalFrameResult(frame, warnings.Distinct().ToList(), shown);
    }

    /// <summary>True when any screen's current page is no longer the page that was last drawn.</summary>
    public bool PagesChanged(IReadOnlyDictionary<Guid, (Guid MenuId, int Page, int Count)> shown)
    {
        foreach (var (screenId, s) in shown)
        {
            if (s.Count <= 1 || _findMenu(s.MenuId) is not { } menu) continue;
            if (_clock.PageFor(screenId, s.Count, PageClock.Period(menu.Theme.PageSeconds), _now()) != s.Page) return true;
        }
        return false;
    }
}
