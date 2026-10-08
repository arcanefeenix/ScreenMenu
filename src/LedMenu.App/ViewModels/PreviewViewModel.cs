using System.Collections.ObjectModel;
using LedMenu.App.Infrastructure;
using LedMenu.Core.Calibration;
using LedMenu.Core.Screens;

namespace LedMenu.App.ViewModels;

/// <summary>Where the preview gets its picture. The LED output implements it, so the preview can never differ from the wall.</summary>
public interface IFrameSource
{
    PixelBuffer CurrentFrame { get; }
    CanvasSize Canvas { get; }
    IReadOnlyList<CalibrationScreen> DrawnScreens { get; }
    bool IsRunning { get; }
    bool IsBlackout => false;
    string CanvasDescription { get; }
    event Action? FrameChanged;

    /// <summary>Some pixels of <see cref="CurrentFrame"/> changed in place (a playing video); its size and screens did not. Raised at most about ten times a second.</summary>
    event Action? PictureChanged;
}

public sealed record PreviewTarget(string Label, CalibrationScreen? Screen)
{
    public bool IsWholeOutput => Screen == null;
    public override string ToString() => Label;
}

/// <summary>Zoom choice. <see cref="Scale"/> 0 means "fit to the preview box".</summary>
public sealed record PreviewScaleOption(string Label, double Scale)
{
    public override string ToString() => Label;
}

/// <summary>
/// The operator's view of the LED output: the whole canvas scaled to fit, the whole canvas at 100% (actual pixels,
/// scrollable), or one screen at 100% to 400% so the exact pixels of, for example, a 336x672 menu can be inspected.
/// It displays <see cref="IFrameSource.CurrentFrame"/> itself; it never draws anything of its own.
/// </summary>
public sealed class PreviewViewModel : ObservableObject
{
    public const double BoxWidth = 400;       // device-independent units; the preview panel is this wide
    public const double BoxHeight = 560;

    private readonly IFrameSource _source;
    private PreviewTarget _target;
    private PreviewScaleOption _scale;
    private Guid _targetScreenId = Guid.Empty;

    public PreviewViewModel(IFrameSource source)
    {
        _source = source;
        Scales = new ObservableCollection<PreviewScaleOption>
        {
            new("Fit to preview", 0),
            new("100% actual pixels", 1),
            new("200%", 2),
            new("300%", 3),
            new("400%", 4),
        };
        _scale = Scales[0];
        _target = new PreviewTarget(WholeLabel(), null);
        Targets.Add(_target);
        _source.FrameChanged += OnFrameChanged;
        _source.PictureChanged += () => { Revision++; OnPropertyChanged(nameof(Revision)); };
        OnFrameChanged();
    }

    public ObservableCollection<PreviewTarget> Targets { get; } = new();
    public ObservableCollection<PreviewScaleOption> Scales { get; }

    public PreviewTarget SelectedTarget
    {
        get => _target;
        set
        {
            if (value == null || ReferenceEquals(value, _target)) return;
            _target = value;
            _targetScreenId = value.Screen?.Id ?? Guid.Empty;
            // a single screen is most useful at actual pixels; the whole canvas at fit
            _scale = value.IsWholeOutput ? Scales[0] : Scales[1];
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedScale));
            RaiseView();
        }
    }

    public PreviewScaleOption SelectedScale
    {
        get => _scale;
        set { if (value != null && !ReferenceEquals(value, _scale)) { _scale = value; OnPropertyChanged(); RaiseView(); } }
    }

    /// <summary>Counts in-place picture changes so the view knows to redraw even though <see cref="Frame"/> is the same object.</summary>
    public int Revision { get; private set; }

    /// <summary>The picture exactly as it is (or will be) on the LED output.</summary>
    public PixelBuffer Frame => _source.CurrentFrame;

    /// <summary>The part of the picture being shown: the whole canvas, or one screen's rectangle.</summary>
    public PixelRegion Region
    {
        get
        {
            var c = _source.Canvas;
            var s = _target.Screen;
            return s == null ? new PixelRegion(0, 0, c.Width, c.Height)
                             : PreviewMath.ClampToCanvas(s.X, s.Y, s.Width, s.Height, c.Width, c.Height);
        }
    }

    /// <summary>Picture pixels to screen pixels (1 = actual pixels). Fit is worked out from the region and the preview box.</summary>
    public double EffectiveScale
    {
        get
        {
            if (_scale.Scale > 0) return _scale.Scale;
            var r = Region;
            return PreviewMath.FitScale(r.Width, r.Height, BoxWidth, BoxHeight);
        }
    }

    /// <summary>What the control is asked to draw: the scale, or 0 to fit.</summary>
    public double Scale => _scale.Scale;

    public bool IsLive => _source.IsRunning;
    public string LiveText => _source.IsRunning ? (_source.IsBlackout ? "BLACKOUT" : "LIVE") : "STOPPED";
    public string CanvasDescription => _source.CanvasDescription;

    public string Status
    {
        get
        {
            var r = Region;
            var what = _target.IsWholeOutput
                ? $"Whole output {_source.Canvas}"
                : $"{_target.Label}: region {r}";
            return $"{what} — {PreviewMath.DescribeScale(EffectiveScale)}";
        }
    }

    private static string WholeLabel() => "Whole output";

    private void OnFrameChanged()
    {
        RebuildTargets();
        RaiseView();
    }

    /// <summary>One entry for the whole output plus one per drawn screen; keeps the selection if the same screen is still there.</summary>
    private void RebuildTargets()
    {
        var wanted = new List<PreviewTarget> { new(WholeLabel(), null) };
        foreach (var s in _source.DrawnScreens)
            wanted.Add(new PreviewTarget($"Screen {s.Number}: {s.Name} ({s.Width}×{s.Height})", s));

        var same = wanted.Count == Targets.Count &&
                   wanted.SequenceEqual(Targets);   // records compare label and screen rectangle, so a moved screen is picked up
        if (!same)
        {
            Targets.Clear();
            foreach (var t in wanted) Targets.Add(t);
        }

        var current = Targets.FirstOrDefault(t => (t.Screen?.Id ?? Guid.Empty) == _targetScreenId) ?? Targets[0];
        if (ReferenceEquals(current, _target)) return;

        var wasWhole = _target.IsWholeOutput;
        _target = current;
        _targetScreenId = current.Screen?.Id ?? Guid.Empty;
        if (wasWhole != current.IsWholeOutput) _scale = current.IsWholeOutput ? Scales[0] : Scales[1];
        OnPropertyChanged(nameof(SelectedTarget));
        OnPropertyChanged(nameof(SelectedScale));
    }

    private void RaiseView()
    {
        OnPropertyChanged(nameof(Frame));
        OnPropertyChanged(nameof(Region));
        OnPropertyChanged(nameof(Scale));
        OnPropertyChanged(nameof(EffectiveScale));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(LiveText));
        OnPropertyChanged(nameof(CanvasDescription));
    }
}
