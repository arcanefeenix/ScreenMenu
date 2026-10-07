using System.Windows.Threading;
using LedMenu.App.Infrastructure;
using LedMenu.App.Output;
using LedMenu.Core.Calibration;
using LedMenu.Core.Display;
using LedMenu.Core.Layout;
using LedMenu.Core.Logging;
using LedMenu.Core.Screens;

namespace LedMenu.App.ViewModels;

/// <summary>
/// Start / Stop for LED output, plus the output mode (normal, identify, screen calibration, canvas calibration).
/// Starting is refused (with a reason, and nothing opened) unless the saved output display is connected right now.
/// Output never moves to a different display. Modes only change what is drawn; they never write configuration.
/// The current output picture is built whether or not output is running, so the operator's preview shows exactly
/// what the LED shows (or will show); only the output window is conditional.
/// </summary>
public sealed class OutputViewModel : ObservableObject, IFrameSource
{
    private static readonly TimeSpan IdentifyDuration = TimeSpan.FromSeconds(15);
    private static readonly CanvasSize StandInCanvas = new(1920, 1080);

    private readonly OutputController _controller;
    private readonly DisplaysViewModel _displays;
    private readonly ScreensViewModel _screens;
    private readonly MenusViewModel _menus;
    private readonly NormalFrameBuilder _builder;
    private readonly PageClock _pageClock = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly DispatcherTimer _rotationTimer;
    private IReadOnlyDictionary<Guid, (Guid MenuId, int Page, int Count)> _shown =
        new Dictionary<Guid, (Guid, int, int)>();
    private string? _contentWarnings;
    private readonly StopHotKey _hotKey;
    private readonly IAppLog _log;
    private readonly Func<IReadOnlyList<string>, string, bool> _confirm;
    private readonly DispatcherTimer _identifyTimer;
    private readonly DispatcherTimer _redrawDebounce;
    private string? _message;
    private bool _isRunning;
    private OutputMode _mode = OutputMode.Normal;
    private string? _skippedText;
    private int _frameRequested;
    private int _frameShown;
    private CanvasSize _canvas = StandInCanvas;
    private PixelBuffer? _blank;
    private readonly BlackoutState _blackout = new();

    public OutputViewModel(OutputController controller, DisplaysViewModel displays, ScreensViewModel screens,
        MenusViewModel menus, MenuRenderService render,
        StopHotKey hotKey, IAppLog log, Func<IReadOnlyList<string>, string, bool> confirm)
    {
        _menus = menus;
        _controller = controller;
        _displays = displays;
        _screens = screens;
        _hotKey = hotKey;
        _log = log;
        _confirm = confirm;
        _builder = new NormalFrameBuilder(menus.Find, render, _pageClock, () => _clock.Elapsed);

        StartCommand = new RelayCommand(Start, () => !_isRunning);
        StopCommand = new RelayCommand(() => Stop("Stop requested by the operator"), () => _isRunning);
        DismissMessageCommand = new RelayCommand(() => Message = null);
        SetModeCommand = new RelayCommand<string>(name =>
        {
            if (Enum.TryParse<OutputMode>(name, out var m)) SetMode(m);
        });
        BlackoutCommand = new RelayCommand(ToggleBlackout, () => _blackout.CanToggle(_isRunning));
        ToggleIdentifyCommand = new RelayCommand(() =>
            SetMode(_mode == OutputMode.IdentifyScreens ? OutputMode.Normal : OutputMode.IdentifyScreens));

        _identifyTimer = new DispatcherTimer { Interval = IdentifyDuration };
        _identifyTimer.Tick += (_, _) =>
        {
            _identifyTimer.Stop();
            if (_mode == OutputMode.IdentifyScreens) SetMode(OutputMode.Normal);
        };

        // Screens or menus edited: redraw once things settle (dragging and typing fire many changes).
        _redrawDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _redrawDebounce.Tick += (_, _) => { _redrawDebounce.Stop(); RenderFrame(); };
        _screens.Changed += RequestRedraw;
        _menus.MenusChanged += RequestRedraw;
        _menus.ContentChanged += RequestRedraw;

        // Pages rotate on elapsed time; this timer only notices when a screen's page has changed and redraws.
        _rotationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _rotationTimer.Tick += (_, _) => { if (_mode == OutputMode.Normal && _builder.PagesChanged(_shown)) RenderFrame(); };
        _rotationTimer.Start();

        _controller.StateChanged += OnStateChanged;
        _controller.StopRequested += () => Stop("Escape pressed on the output window");
        _hotKey.Pressed += () => Stop("Ctrl+Shift+F12");
        _hotKey.BlackoutPressed += ToggleBlackout;
        _hotKey.IdentifyPressed += () => ToggleIdentifyCommand.Execute(null);
        _displays.Refreshed += OnDisplaysRefreshed;
    }

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand DismissMessageCommand { get; }
    public RelayCommand<string> SetModeCommand { get; }
    public RelayCommand ToggleIdentifyCommand { get; }
    public RelayCommand BlackoutCommand { get; }

    public bool IsRunning { get => _isRunning; private set { if (Set(ref _isRunning, value)) BlackoutCommand.RaiseCanExecuteChanged(); } }

    /// <summary>Why output did not start or was stopped. Null when there is nothing to report.</summary>
    public string? Message
    {
        get => _message;
        private set { if (Set(ref _message, value)) OnPropertyChanged(nameof(HasMessage)); }
    }
    public bool HasMessage => !string.IsNullOrEmpty(_message);

    // ---- blackout ---------------------------------------------------------------------------

    public bool IsBlackout => _blackout.IsActive;

    /// <summary>Big button text: says what pressing it does.</summary>
    public string BlackoutButtonText => _blackout.IsActive ? "END BLACKOUT — SHOW MENUS AGAIN" : "BLACKOUT";

    public string BlackoutBannerText => _blackout.IsActive
        ? "BLACKOUT IS ON: the LED output is pure black. Your menus and settings are untouched. Press Ctrl+Shift+B or the button to bring the display back."
        : "";

    /// <summary>Switches the LED output to pure black and back. Only while output is running; never changes any menu or setting.</summary>
    public void ToggleBlackout()
    {
        if (!_blackout.Toggle(_isRunning)) return;
        _log.Info(_blackout.IsActive ? "Blackout ON" : "Blackout OFF");
        BlackoutChanged();
        // the picture underneath is untouched and still current; just change what the wall is given
        _controller.ShowFrame(_blackout.Apply(LastFrame));
        FrameChanged?.Invoke();
    }

    private void BlackoutChanged()
    {
        OnPropertyChanged(nameof(IsBlackout));
        OnPropertyChanged(nameof(BlackoutButtonText));
        OnPropertyChanged(nameof(BlackoutBannerText));
        OnPropertyChanged(nameof(CanvasDescription));
        _displays.SetBlackout(_blackout.IsActive);
        BlackoutCommand.RaiseCanExecuteChanged();
    }

    // ---- output mode ------------------------------------------------------------------------

    public OutputMode Mode => _mode;
    public bool IsNormalMode => _mode == OutputMode.Normal;
    public bool IsIdentifyMode => _mode == OutputMode.IdentifyScreens;
    public bool IsScreenCalibrationMode => _mode == OutputMode.ScreenCalibration;
    public bool IsCanvasCalibrationMode => _mode == OutputMode.OutputCanvasCalibration;
    public bool IsTestMode => _mode != OutputMode.Normal;

    /// <summary>Prominent banner shown whenever a test pattern is selected or on the LED output.</summary>
    public string ModeBannerText
    {
        get
        {
            if (_mode == OutputMode.Normal) return "";
            var name = ModeName(_mode);
            var tail = _mode == OutputMode.IdentifyScreens ? " Returns to Normal Output automatically after 15 seconds." : "";
            return _isRunning
                ? $"TEST PATTERN ON THE LED OUTPUT: {name.ToUpperInvariant()}. Menus are not being shown.{tail}"
                : $"Test mode selected: {name}. It will be sent to the LED output when output starts.{tail}";
        }
    }

    /// <summary>Screens that were not drawn in the current test pattern because they are invalid.</summary>
    public string? SkippedText { get => _skippedText; private set { if (Set(ref _skippedText, value)) OnPropertyChanged(nameof(HasSkipped)); } }
    public bool HasSkipped => !string.IsNullOrEmpty(_skippedText);

    // ---- the current picture (shared by the LED output and the preview) -----------------------

    /// <summary>The picture for the current mode, or null while it is pure black (normal output with nothing assigned).</summary>
    public PixelBuffer? LastFrame { get; private set; }

    /// <summary>The picture, never null: a black canvas of the right size when nothing is drawn. This is what the preview shows.</summary>
    public PixelBuffer CurrentFrame => _blackout.IsActive ? Blank(_canvas) : LastFrame ?? Blank(_canvas);

    /// <summary>Size of the output canvas the picture is built for.</summary>
    public CanvasSize Canvas => _canvas;

    /// <summary>The screens that are drawn in the current picture (enabled and valid), in list order.</summary>
    public IReadOnlyList<CalibrationScreen> DrawnScreens { get; private set; } = Array.Empty<CalibrationScreen>();

    /// <summary>Raised whenever <see cref="CurrentFrame"/>, its size or its screens changed.</summary>
    public event Action? FrameChanged;

    /// <summary>One line saying what the picture is: live output, a stopped preview, or a stand-in size.</summary>
    public string CanvasDescription
    {
        get
        {
            var info = _screens.Canvas;
            if (_isRunning && _blackout.IsActive) return $"BLACKOUT — the LED output is pure black (canvas {_canvas})";
            if (_isRunning) return $"LIVE on the LED output — canvas {_canvas}";
            return info.Source switch
            {
                CanvasSource.Live => $"Output stopped — preview of canvas {_canvas} ({info.DisplayName})",
                CanvasSource.LastKnown => $"Output display not connected — preview of its last known size {_canvas}",
                _ => $"No output display selected — preview on a {_canvas} stand-in canvas",
            };
        }
    }

    /// <summary>False while a new frame is being prepared or has not reached the output window yet.</summary>
    public bool FrameUpToDate => _frameShown == _frameRequested;

    public void SetMode(OutputMode mode)
    {
        _identifyTimer.Stop();
        if (mode == OutputMode.IdentifyScreens) _identifyTimer.Start();

        if (_mode != mode)
        {
            if (mode == OutputMode.Normal) _pageClock.ResetAll();   // leaving a test pattern restarts every menu at page 1
            _mode = mode;
            _log.Info($"Output mode: {ModeName(mode)}");
        }
        RaiseModeChanged();
        RenderFrame();
    }

    private void RaiseModeChanged()
    {
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(IsNormalMode));
        OnPropertyChanged(nameof(IsIdentifyMode));
        OnPropertyChanged(nameof(IsScreenCalibrationMode));
        OnPropertyChanged(nameof(IsCanvasCalibrationMode));
        OnPropertyChanged(nameof(IsTestMode));
        OnPropertyChanged(nameof(ModeBannerText));
        OnPropertyChanged(nameof(CanvasDescription));
        _displays.SetTestLabel(_isRunning && _mode != OutputMode.Normal ? ModeName(_mode).ToUpperInvariant() : null);
    }

    public static string ModeName(OutputMode m) => m switch
    {
        OutputMode.Normal => "Normal Output",
        OutputMode.IdentifyScreens => "Identify Screens",
        OutputMode.ScreenCalibration => "Screen Calibration",
        OutputMode.OutputCanvasCalibration => "Output Canvas Calibration",
        _ => m.ToString(),
    };

    /// <summary>The canvas size the picture is built for: the real output when running, otherwise the best known size.</summary>
    private CanvasSize CurrentCanvas() =>
        _isRunning && _controller.Display is { } d ? new CanvasSize(d.Width, d.Height)
        : _screens.Canvas.Size ?? StandInCanvas;

    /// <summary>
    /// Builds the picture for the current mode (test patterns off the UI thread) and keeps it as the current frame.
    /// If output is running it is also put on the output window.
    /// </summary>
    private void RenderFrame()
    {
        var requested = ++_frameRequested;
        var canvas = CurrentCanvas();
        var mode = _mode;
        var plan = _screens.BuildCalibrationPlan(canvas);

        SkippedText = mode is OutputMode.IdentifyScreens or OutputMode.ScreenCalibration && plan.Skipped.Count > 0
            ? "Not drawn because they are invalid: " + string.Join("; ", plan.Skipped.Select(s => $"{s.Number} {s.Name} ({s.Reason})"))
            : null;

        if (mode == OutputMode.Normal)
        {
            try
            {
                var result = _builder.Build(canvas, plan);
                _shown = result.Shown;
                ContentWarnings = result.Warnings.Count == 0 ? null : string.Join("\n", result.Warnings);
                Publish(result.Frame, canvas, plan.Drawn);
            }
            catch (Exception ex)
            {
                // a menu that cannot be drawn must never blank or crash the wall: keep what is showing and tell the operator
                _log.Error("A menu could not be drawn; the previous picture is being kept.", ex);
                Message = "A menu could not be drawn, so the previous picture was kept on the LED output: " + ex.Message;
            }
            _frameShown = requested;
            return;
        }

        ContentWarnings = null;
        _shown = new Dictionary<Guid, (Guid, int, int)>();

        var dispatcher = System.Windows.Application.Current.Dispatcher;
        _ = Task.Run(() =>
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var frame = FrameComposer.Compose(mode, canvas.Width, canvas.Height, plan.Drawn);
                sw.Stop();
                dispatcher.InvokeAsync(() =>
                {
                    if (requested != _frameRequested) return;   // superseded by a newer request
                    Publish(frame, canvas, plan.Drawn);
                    _frameShown = requested;
                    _log.Info($"Frame built: {ModeName(mode)} {canvas} with {plan.Drawn.Count} screen(s), {plan.Skipped.Count} skipped, in {sw.ElapsedMilliseconds} ms.");
                });
            }
            catch (Exception ex)
            {
                _log.Error("Could not build the output frame.", ex);
                dispatcher.InvokeAsync(() => Message = "The test pattern could not be built: " + ex.Message);
            }
        });
    }

    /// <summary>Makes a built picture current: remember it for the preview and, if running, send it to the LED output.</summary>
    private void Publish(PixelBuffer? frame, CanvasSize canvas, IReadOnlyList<CalibrationScreen> drawn)
    {
        _canvas = canvas;
        LastFrame = frame;
        DrawnScreens = drawn;
        if (_isRunning) _controller.ShowFrame(_blackout.Apply(frame));          // null = pure black
        OnPropertyChanged(nameof(CanvasDescription));
        FrameChanged?.Invoke();
    }

    private PixelBuffer Blank(CanvasSize size)
    {
        if (_blank == null || _blank.Width != size.Width || _blank.Height != size.Height)
            _blank = new PixelBuffer(size.Width, size.Height);
        return _blank;
    }

    // ---- normal output: menus -------------------------------------------------------------------

    /// <summary>Problems with the menus in the current picture (missing logo, overflowing item, font fallback). Null when there are none.</summary>
    public string? ContentWarnings
    {
        get => _contentWarnings;
        private set { if (Set(ref _contentWarnings, value)) OnPropertyChanged(nameof(HasContentWarnings)); }
    }
    public bool HasContentWarnings => !string.IsNullOrEmpty(_contentWarnings);

    /// <summary>The page of this menu that is showing right now (zero-based), or null if it is not in the current picture.</summary>
    public (int Page, int Count)? LivePageOf(Guid menuId)
    {
        if (_mode != OutputMode.Normal) return null;
        foreach (var shown in _shown.Values)
            if (shown.MenuId == menuId) return (shown.Page, shown.Count);
        return null;
    }

    /// <summary>Builds the current picture now (used once at start-up so the preview is not empty).</summary>
    public void Refresh() => RenderFrame();

    private void RequestRedraw()
    {
        _redrawDebounce.Stop();
        _redrawDebounce.Start();
    }

    // ---- start / stop -------------------------------------------------------------------------

    public void Start()
    {
        if (_isRunning) return;

        _displays.Refresh();                 // decide against the displays as they are right now
        var decision = _displays.EvaluateStart();
        if (!decision.CanStart)
        {
            _log.Warn($"Output start refused: {decision.Refusal}");
            Message = decision.Refusal;
            return;
        }

        if (decision.Warnings.Count > 0 &&
            !_confirm(decision.Warnings, $"Start LED output on \"{decision.Display!.FriendlyName}\" ({decision.Display.Resolution})?"))
        {
            _log.Info("Output start cancelled by the operator at the warning prompt.");
            return;
        }

        // Register the stop shortcut first so a covered operator window can always be recovered.
        _hotKey.Register();
        if (!_controller.Start(decision.Display!))
        {
            _hotKey.Unregister();
            Message = "The output window could not be opened. See the log for details.";
            return;
        }
        Message = null;
    }

    public void Stop(string reason)
    {
        if (!_isRunning) return;
        _controller.Stop(reason);
    }

    private void OnStateChanged(bool running, string? reason)
    {
        IsRunning = running;
        _displays.SetOutputRunning(running);
        if (!running)
        {
            if (_blackout.OutputStopped()) _log.Info("Output stopped; blackout cleared.");
            _hotKey.Unregister();
            _identifyTimer.Stop();
            // A stopped output always returns to Normal, so a test pattern can never reappear by surprise at the next start.
            if (_mode != OutputMode.Normal) _log.Info($"Output stopped; mode reset from {ModeName(_mode)} to Normal Output.");
            _mode = OutputMode.Normal;
            SkippedText = null;
        }
        if (!running && reason != null && reason.StartsWith("The output window was closed"))
            Message = reason;
        BlackoutChanged();
        RaiseModeChanged();
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();

        _pageClock.ResetAll();                      // every menu starts at page 1 when output starts (and the preview restarts when it stops)
        RenderFrame();
    }

    private void OnDisplaysRefreshed()
    {
        if (!_isRunning)
        {
            // the preview follows the best known canvas size
            if (CurrentCanvas() != _canvas) RenderFrame();
            else OnPropertyChanged(nameof(CanvasDescription));
            return;
        }

        var match = _displays.OutputMatch;
        if (match.Kind != MatchKind.Exact || match.Display is null)
        {
            _log.Warn("LED output display is no longer available; stopping output.");
            Stop("The LED output display was disconnected or changed");
            Message = "LED output was stopped because the output display is no longer available. " +
                      "It was not moved to another display.";
            return;
        }

        var cur = _controller.Display;
        var now = match.Display;
        if (cur is not null && (cur.X != now.X || cur.Y != now.Y || cur.Width != now.Width || cur.Height != now.Height))
        {
            _log.Info($"LED output display changed geometry ({cur.Width}x{cur.Height} -> {now.Width}x{now.Height}); repositioning.");
            _controller.Reposition(now);
            RenderFrame();
        }
    }
}
