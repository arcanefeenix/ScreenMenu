using LedMenu.App.ViewModels;
using LedMenu.Core.Calibration;
using LedMenu.Core.Screens;

namespace LedMenu.App.Tests;

public class PreviewViewModelTests
{
    private sealed class FakeSource : IFrameSource
    {
        public PixelBuffer CurrentFrame { get; set; } = new(1920, 1080);
        public CanvasSize Canvas { get; set; } = new(1920, 1080);
        public IReadOnlyList<CalibrationScreen> DrawnScreens { get; set; } = Array.Empty<CalibrationScreen>();
        public bool IsRunning { get; set; }
        public string CanvasDescription => "test canvas";
        public event Action? FrameChanged;
        public void Raise() => FrameChanged?.Invoke();
    }

    private static CalibrationScreen Scr(int n, int x, int y, int w, int h) => new(Guid.NewGuid(), n, "S" + n, x, y, w, h);

    [Fact]
    public void Starts_on_the_whole_output_fitted_and_shows_the_sources_own_frame()
    {
        var src = new FakeSource();
        var vm = new PreviewViewModel(src);
        Assert.True(vm.SelectedTarget.IsWholeOutput);
        Assert.Equal(0, vm.Scale);
        Assert.Same(src.CurrentFrame, vm.Frame);                     // the exact same picture, not a copy
        Assert.Equal(new PixelRegion(0, 0, 1920, 1080), vm.Region);
        Assert.Equal("STOPPED", vm.LiveText);
    }

    [Fact]
    public void Screens_appear_as_targets_and_selecting_one_shows_its_rectangle_at_100_percent()
    {
        var src = new FakeSource { DrawnScreens = new[] { Scr(1, 0, 0, 336, 672) } };
        var vm = new PreviewViewModel(src);
        src.Raise();
        Assert.Equal(2, vm.Targets.Count);
        vm.SelectedTarget = vm.Targets[1];
        Assert.Equal(new PixelRegion(0, 0, 336, 672), vm.Region);
        Assert.Equal(1, vm.Scale);
        Assert.Equal(1, vm.EffectiveScale);
    }

    [Fact]
    public void Selection_survives_a_refresh_and_follows_the_screen_if_it_moves()
    {
        var s = Scr(1, 0, 0, 336, 672);
        var src = new FakeSource { DrawnScreens = new[] { s } };
        var vm = new PreviewViewModel(src);
        src.Raise();
        vm.SelectedTarget = vm.Targets[1];
        src.DrawnScreens = new[] { s with { X = 100 } };
        src.Raise();
        Assert.Equal(new PixelRegion(100, 0, 336, 672), vm.Region);
    }

    [Fact]
    public void If_the_selected_screen_disappears_it_falls_back_to_the_whole_output_fitted()
    {
        var src = new FakeSource { DrawnScreens = new[] { Scr(1, 0, 0, 336, 672) } };
        var vm = new PreviewViewModel(src);
        src.Raise();
        vm.SelectedTarget = vm.Targets[1];
        src.DrawnScreens = Array.Empty<CalibrationScreen>();
        src.Raise();
        Assert.True(vm.SelectedTarget.IsWholeOutput);
        Assert.Equal(0, vm.Scale);
    }

    [Fact]
    public void Frame_replacement_and_live_state_are_reported_to_the_view()
    {
        var src = new FakeSource();
        var vm = new PreviewViewModel(src);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var next = new PixelBuffer(1920, 1080);
        src.CurrentFrame = next;
        src.IsRunning = true;
        src.Raise();
        Assert.Same(next, vm.Frame);
        Assert.Contains(nameof(vm.Frame), changed);
        Assert.Equal("LIVE", vm.LiveText);
        Assert.True(vm.IsLive);
    }

    [Fact]
    public void A_screen_partly_off_the_canvas_previews_only_the_part_that_exists()
    {
        var src = new FakeSource { DrawnScreens = new[] { Scr(1, 1800, 0, 336, 672) } };
        var vm = new PreviewViewModel(src);
        src.Raise();
        vm.SelectedTarget = vm.Targets[1];
        Assert.Equal(new PixelRegion(1800, 0, 120, 672), vm.Region);
    }

    [Fact]
    public void Status_says_what_is_shown_and_at_what_scale()
    {
        var src = new FakeSource { DrawnScreens = new[] { Scr(1, 0, 0, 336, 672) } };
        var vm = new PreviewViewModel(src);
        src.Raise();
        vm.SelectedTarget = vm.Targets[1];
        Assert.Contains("100%", vm.Status);
        vm.SelectedScale = vm.Scales.First(o => o.Scale == 3);
        Assert.Contains("300%", vm.Status);
    }
}
