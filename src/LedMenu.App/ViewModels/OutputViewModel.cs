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
/// </summary>
public sealed class OutputViewModel : ObservableObject
{
    private static readonly TimeSpan IdentifyDuration = TimeSpan.FromSeconds(15);

    private readonly OutputController _controller;
    private readonly DisplaysViewModel _displays;
    private readonly ScreensViewModel _screens;
    private readonly MenusViewModel _menus;
    private readonly MenuRenderService _render;
    private readonly PageClock _pageClock = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly DispatcherTimer _rotationTimer;
    private readonly Dictionary<Guid, (Guid MenuId, int Page, int Count)> _shown = new();
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

    public OutputViewModel(OutputController controller, DisplaysViewModel displays, ScreensViewModel screens,
        MenusViewModel menus, MenuRenderService render,
        StopHotKey hotKey, IAppLog log, Func<IReadOnlyList<string>, string, bool> confirm)
    {
        _menus = menus;
        _render = render;
        _controller = controller;
        _displays = displays;
        _screens = screens;
        _hotKey = hotKey;
        _log = log;
        _confirm = confirm;

        StartCommand = new RelayCommand(Start, () => !_isRunning);
        StopCommand = new RelayCommand(() => Stop("Stop requested by the operator"), () => _isRunning);
        DismissMessageCommand = new RelayCommand(() => Message = null);
        SetModeCommand = new RelayCommand<string>(name =>
        {
            if (Enum.TryParse<OutputMode>(name, out var m)) SetMode(m);
        });
        ToggleIdentifyCommand = new RelayCommand(() =>
            SetMode(_mode == OutputMode.IdentifyScreens ? OutputMode.Normal : OutputMode.IdentifyScreens));

        _identifyTimer = new DispatcherTimer { Interval = IdentifyDuration };
        _identifyTimer.Tick += (_, _) =>
        {
            _identifyTimer.Stop();
            if (_mode == OutputMode.IdentifyScreens) SetMode(OutputMode.Normal);
        };

        // Screens edited while a pattern is showing: redraw once things settle (dragging fires many changes).
        _redrawDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _redrawDebounce.Tick += (_, _) => { _redrawDebounce.Stop(); if (_isRunning) RenderFrame(); };
        _screens.Changed += RequestRedraw;
        _menus.MenusChanged += RequestRedraw;
        _menus.ContentChanged += RequestRedraw;

        // Pages rotate on elapsed time; this timer only notices when a screen's page has changed and redraws.
        _rotationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _rotationTimer.Tick += (_, _) => { if (_isRunning && _mode == OutputMode.Normal && PagesChanged()) RenderFrame(); };

        _controller.StateChanged += OnStateChanged;
        _controller.StopRequested += () => Stop("Escape pressed on the output window");
        _hotKey.Pressed += () => Stop("Ctrl+Shift+F12");
        _displays.Refreshed += OnDisplaysRefreshed;
    }

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand DismissMessageCommand { get; }
    public RelayCommand<string> SetModeCommand { get; }
    public RelayCommand ToggleIdentifyCommand { get; }

    public bool IsRunning { get => _isRunning; private set => Set(ref _isRunning, value); }

    /// <summary>Why output did not start or was stopped. Null when there is nothing to report.</summary>
    public string? Message
    {
        get => _message;
        private set { if (Set(ref _message, value)) OnPropertyChanged(nameof(HasMessage)); }
    }
    public bool HasMessage => !string.IsNullOrEmpty(_message);

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

    /// <summary>The frame currently on the LED output (null while normal black output or stopped).</summary>
    public PixelBuffer? LastFrame { get; private set; }

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

    /// <summary>Builds the frame for the current mode off the UI thread and puts it on the output when ready.</summary>
    private void RenderFrame()
    {
        if (!_isRunning || _controller.Display is not { } display) { SkippedText = null; return; }

        var requested = ++_frameRequested;
        var canvas = new CanvasSize(display.Width, display.Height);
        var mode = _mode;
        var plan = _screens.BuildCalibrationPlan(canvas);

        SkippedText = mode is OutputMode.IdentifyScreens or OutputMode.ScreenCalibration && plan.Skipped.Count > 0
            ? "Not drawn because they are invalid: " + string.Join("; ", plan.Skipped.Select(s => $"{s.Number} {s.Name} ({s.Reason})"))
            : null;

        if (mode == OutputMode.Normal)
        {
            try
            {
                var normal = BuildNormalFrame(canvas, plan);
                LastFrame = normal;
                _controller.ShowFrame(normal);          // null = pure black
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
                    if (requested != _frameRequested || !_isRunning) return;   // superseded or output stopped
                    LastFrame = frame;
                    _controller.ShowFrame(frame);
                    _frameShown = requested;
                    _log.Info($"Frame shown: {ModeName(mode)} {canvas} with {plan.Drawn.Count} screen(s), {plan.Skipped.Count} skipped, built in {sw.ElapsedMilliseconds} ms.");
                });
            }
            catch (Exception ex)
            {
                _log.Error("Could not build the output frame.", ex);
                dispatcher.InvokeAsync(() => Message = "The test pattern could not be built: " + ex.Message);
            }
        });
    }

    // ---- normal output: menus -------------------------------------------------------------------

    /// <summary>Problems with the menus currently on the LED output (missing logo, overflowing item, font fallback). Null when there are none.</summary>
    public string? ContentWarnings
    {
        get => _contentWarnings;
        private set { if (Set(ref _contentWarnings, value)) OnPropertyChanged(nameof(HasContentWarnings)); }
    }
    public bool HasContentWarnings => !string.IsNullOrEmpty(_contentWarnings);

    private TimeSpan Now => _clock.Elapsed;

    /// <summary>
    /// Black canvas with each assigned menu's current page placed at its screen. Every page is the screen's own pixel size.
    /// Screens with no menu, a missing menu, or errors stay black. Null when nothing at all is drawn.
    /// </summary>
    private PixelBuffer? BuildNormalFrame(CanvasSize canvas, CalibrationPlan plan)
    {
        var parts = new List<(int, int, PixelBuffer)>();
        var warnings = new List<string>();
        _shown.Clear();

        foreach (var s in plan.Drawn)
        {
            if (s.MenuId is not { } menuId || _menus.Find(menuId) is not { } menu) continue;

            var result = _render.Get(menu, s.Width, s.Height);
            foreach (var p in result.Problems) warnings.Add($"{menu.Name}: {p.Message}");

            var page = _pageClock.PageFor(s.Id, result.PageCount, PageClock.Period(menu.Theme.PageSeconds), Now);
            _shown[s.Id] = (menuId, page, result.PageCount);
            parts.Add((s.X, s.Y, result.Pages[Math.Min(page, result.PageCount - 1)]));
        }

        ContentWarnings = warnings.Count == 0 ? null : string.Join("\n", warnings.Distinct());
        return parts.Count == 0 ? null : FrameComposer.ComposeScreens(canvas.Width, canvas.Height, parts);
    }

    /// <summary>True when any screen's current page is no longer the page that is on the wall.</summary>
    private bool PagesChanged()
    {
        foreach (var (screenId, shown) in _shown)
        {
            if (shown.Count <= 1 || _menus.Find(shown.MenuId) is not { } menu) continue;
            var page = _pageClock.PageFor(screenId, shown.Count, PageClock.Period(menu.Theme.PageSeconds), Now);
            if (page != shown.Page) return true;
        }
        return false;
    }

    /// <summary>The page of this menu that is on the LED output right now (zero-based), or null if it is not being shown.</summary>
    public (int Page, int Count)? LivePageOf(Guid menuId)
    {
        if (!_isRunning || _mode != OutputMode.Normal) return null;
        foreach (var shown in _shown.Values)
            if (shown.MenuId == menuId) return (shown.Page, shown.Count);
        return null;
    }

    private void RequestRedraw()
    {
        if (!_isRunning) return;
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
            _hotKey.Unregister();
            _identifyTimer.Stop();
            _redrawDebounce.Stop();
            _rotationTimer.Stop();
            _shown.Clear();
            ContentWarnings = null;
            // A stopped output always returns to Normal, so a test pattern can never reappear by surprise at the next start.
            if (_mode != OutputMode.Normal) _log.Info($"Output stopped; mode reset from {ModeName(_mode)} to Normal Output.");
            _mode = OutputMode.Normal;
            LastFrame = null;
            SkippedText = null;
            _frameShown = _frameRequested;
        }
        if (!running && reason != null && reason.StartsWith("The output window was closed"))
            Message = reason;
        RaiseModeChanged();
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        if (running)
        {
            _pageClock.ResetAll();                  // every menu starts at page 1 when output starts
            _rotationTimer.Start();
            RenderFrame();
        }
    }

    private void OnDisplaysRefreshed()
    {
        if (!_isRunning) return;

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
