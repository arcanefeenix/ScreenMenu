using LedMenu.App.Infrastructure;
using LedMenu.App.Output;
using LedMenu.Core.Display;
using LedMenu.Core.Logging;

namespace LedMenu.App.ViewModels;

/// <summary>
/// Start / Stop for LED output. Starting is refused (with a reason, and nothing opened) unless the saved
/// output display is connected right now. Output never moves to a different display, and starting or
/// stopping never writes configuration.
/// </summary>
public sealed class OutputViewModel : ObservableObject
{
    private readonly OutputController _controller;
    private readonly DisplaysViewModel _displays;
    private readonly StopHotKey _hotKey;
    private readonly IAppLog _log;
    private readonly Func<IReadOnlyList<string>, string, bool> _confirm;
    private string? _message;
    private bool _isRunning;

    public OutputViewModel(OutputController controller, DisplaysViewModel displays, StopHotKey hotKey,
        IAppLog log, Func<IReadOnlyList<string>, string, bool> confirm)
    {
        _controller = controller;
        _displays = displays;
        _hotKey = hotKey;
        _log = log;
        _confirm = confirm;

        StartCommand = new RelayCommand(Start, () => !_isRunning);
        StopCommand = new RelayCommand(() => Stop("Stop requested by the operator"), () => _isRunning);
        DismissMessageCommand = new RelayCommand(() => Message = null);

        _controller.StateChanged += OnStateChanged;
        _controller.StopRequested += () => Stop("Escape pressed on the output window");
        _hotKey.Pressed += () => Stop("Ctrl+Shift+F12");
        _displays.Refreshed += OnDisplaysRefreshed;
    }

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand DismissMessageCommand { get; }

    public bool IsRunning { get => _isRunning; private set => Set(ref _isRunning, value); }

    /// <summary>Why output did not start or was stopped. Null when there is nothing to report.</summary>
    public string? Message
    {
        get => _message;
        private set { if (Set(ref _message, value)) OnPropertyChanged(nameof(HasMessage)); }
    }
    public bool HasMessage => !string.IsNullOrEmpty(_message);

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
        if (!running) _hotKey.Unregister();
        if (!running && reason != null && reason.StartsWith("The output window was closed"))
            Message = reason;
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
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
        }
    }
}
