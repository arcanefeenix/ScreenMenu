using System.Collections.ObjectModel;
using System.Windows.Threading;
using LedMenu.App.Infrastructure;
using LedMenu.Core.Display;
using LedMenu.Core.Logging;
using LedMenu.Core.Models;
using Microsoft.Win32;

namespace LedMenu.App.ViewModels;

public sealed class DisplayItemViewModel
{
    public required int Number { get; init; }
    public required string FriendlyName { get; init; }
    public required string Resolution { get; init; }
    public required string Position { get; init; }
    public required string Scaling { get; init; }
    public required string WindowsName { get; init; }
    public required string DeviceId { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required RelayCommand SetOutputCommand { get; init; }
    public required RelayCommand SetOperatorCommand { get; init; }
    public bool IsLedOutput { get; init; }
    public bool IsOperator { get; init; }
}

/// <summary>
/// Lists connected displays, remembers which one is the LED output and which is the operator display,
/// and reports plainly when the chosen LED output is not connected. It never picks a replacement.
/// </summary>
public sealed class DisplaysViewModel : ObservableObject, IDisposable
{
    private readonly IDisplaySource _source;
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private readonly IAppLog _log;
    private readonly Func<IReadOnlyList<string>, string, bool> _confirm;
    private readonly Func<long> _operatorWindowMonitor;
    private readonly Action<DisplayInfo, int> _identify;
    private readonly DispatcherTimer _debounce;

    private IReadOnlyList<DisplayInfo> _current = Array.Empty<DisplayInfo>();
    private MatchResult _output = new(MatchKind.NotConfigured, null);
    private MatchResult _operator = new(MatchKind.NotConfigured, null);
    private string _lastSignature = "";
    private MatchKind? _lastOutputKind;
    private string _outputChip = "OUTPUT: NO DISPLAY SELECTED";
    private string? _outputAlert;
    private string? _operatorNote;
    private bool _outputRunning;
    private string? _testLabel;
    private bool _blackout;

    public DisplaysViewModel(IDisplaySource source, AppSettings settings, Action saveSettings, IAppLog log,
        Func<IReadOnlyList<string>, string, bool> confirm, Func<long> operatorWindowMonitor,
        Action<DisplayInfo, int> identify)
    {
        _source = source;
        _settings = settings;
        _saveSettings = saveSettings;
        _log = log;
        _confirm = confirm;
        _operatorWindowMonitor = operatorWindowMonitor;
        _identify = identify;

        RefreshCommand = new RelayCommand(Refresh);
        IdentifyCommand = new RelayCommand(IdentifyAll);
        ConfirmFallbackCommand = new RelayCommand(ConfirmFallback);
        ClearOutputCommand = new RelayCommand(ClearOutput, () => _settings.OutputDisplay != null);

        // Windows raises this when monitors are plugged, unplugged or reconfigured; it often fires in bursts.
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Refresh(); };
        SystemEvents.DisplaySettingsChanged += OnSystemDisplayChange;

        Refresh();
    }

    public ObservableCollection<DisplayItemViewModel> Items { get; } = new();

    public RelayCommand RefreshCommand { get; }
    public RelayCommand IdentifyCommand { get; }
    public RelayCommand ConfirmFallbackCommand { get; }
    public RelayCommand ClearOutputCommand { get; }

    /// <summary>Text for the status chip in the header.</summary>
    public string OutputChip { get => _outputChip; private set => Set(ref _outputChip, value); }

    /// <summary>Prominent warning about the LED output display, or null when there is nothing to report.</summary>
    public string? OutputAlert
    {
        get => _outputAlert;
        private set { if (Set(ref _outputAlert, value)) OnPropertyChanged(nameof(HasOutputAlert)); }
    }
    public bool HasOutputAlert => !string.IsNullOrEmpty(_outputAlert);

    public string? OperatorNote
    {
        get => _operatorNote;
        private set { if (Set(ref _operatorNote, value)) OnPropertyChanged(nameof(HasOperatorNote)); }
    }
    public bool HasOperatorNote => !string.IsNullOrEmpty(_operatorNote);

    /// <summary>Raised at the end of every scan so the output controller can react to a lost display.</summary>
    public event Action? Refreshed;

    public MatchResult OutputMatch => _output;

    /// <summary>Decision for starting output against the displays as of the most recent scan.</summary>
    public OutputStartDecision EvaluateStart() =>
        OutputStartPolicy.Evaluate(_output, _current, _operatorWindowMonitor());

    /// <summary>Shown on the status chip while a test pattern is on the LED output; null for normal output.</summary>
    public void SetTestLabel(string? label)
    {
        if (_testLabel == label) return;
        _testLabel = label;
        UpdateStatus();
    }

    /// <summary>Blackout is on: the status chip says so, whatever else is going on.</summary>
    public void SetBlackout(bool on)
    {
        if (_blackout == on) return;
        _blackout = on;
        UpdateStatus();
    }

    public void SetOutputRunning(bool running)
    {
        if (_outputRunning == running) return;
        _outputRunning = running;
        UpdateStatus();
    }

    public bool NeedsFallbackConfirmation => _output.Kind == MatchKind.Fallback;

    /// <summary>The display to use for LED output, or null if it is unset, missing, or awaiting confirmation.</summary>
    public DisplayInfo? ConfirmedOutputDisplay => _output.Kind == MatchKind.Exact ? _output.Display : null;

    public void Refresh()
    {
        IReadOnlyList<DisplayInfo> ordered;
        try
        {
            ordered = DisplayMatcher.Order(_source.GetDisplays());
        }
        catch (Exception ex)
        {
            _log.Error("Display enumeration failed.", ex);
            OutputAlert = "Windows displays could not be read. The LED output stays disabled.";
            return;
        }

        _current = ordered;
        LogIfChanged(ordered);

        _output = DisplayMatcher.Match(_settings.OutputDisplay, ordered);
        _operator = DisplayMatcher.Match(_settings.OperatorDisplay, ordered);
        LogOutputTransition();
        RefreshRememberedGeometry();

        var windowMonitor = _operatorWindowMonitor();
        Items.Clear();
        for (var i = 0; i < ordered.Count; i++)
            Items.Add(BuildItem(ordered[i], i + 1, windowMonitor));

        UpdateStatus();
        OnPropertyChanged(nameof(NeedsFallbackConfirmation));
        OnPropertyChanged(nameof(ConfirmedOutputDisplay));
        ClearOutputCommand.RaiseCanExecuteChanged();
        Refreshed?.Invoke();
    }

    private DisplayItemViewModel BuildItem(DisplayInfo d, int number, long windowMonitor)
    {
        var isOutput = _output.IsAvailable && Same(_output.Display!, d);
        var isOperator = _operator.IsAvailable && Same(_operator.Display!, d);
        var tags = new List<string>();
        if (d.IsPrimary) tags.Add("WINDOWS PRIMARY");
        if (isOperator) tags.Add("OPERATOR DISPLAY");
        if (windowMonitor != 0 && windowMonitor == d.Handle) tags.Add("OPERATOR WINDOW IS HERE");
        if (isOutput) tags.Add(_output.Kind == MatchKind.Fallback ? "LED OUTPUT (UNCONFIRMED)" : "LED OUTPUT");

        return new DisplayItemViewModel
        {
            Number = number,
            FriendlyName = d.FriendlyName,
            Resolution = d.Resolution,
            Position = $"Desktop position {d.X}, {d.Y}",
            Scaling = $"Windows scaling {d.ScalePercent}%",
            WindowsName = d.DeviceName,
            DeviceId = string.IsNullOrEmpty(d.DevicePath) ? "(no stable ID available)" : d.DevicePath,
            Tags = tags,
            IsLedOutput = isOutput,
            IsOperator = isOperator,
            SetOutputCommand = new RelayCommand(() => SetOutput(d)),
            SetOperatorCommand = new RelayCommand(() => SetOperator(d)),
        };
    }

    private void UpdateStatus()
    {
        switch (_output.Kind)
        {
            case MatchKind.NotConfigured:
                OutputChip = "OUTPUT: NO DISPLAY SELECTED";
                OutputAlert = "No LED output display has been selected. Choose the display connected to the LED controller below.";
                break;
            case MatchKind.NotFound:
                OutputChip = "OUTPUT: NO DISPLAY";
                OutputAlert = $"The LED output display \"{_settings.OutputDisplay!.Describe()}\" is not connected. " +
                              "Output is disabled and no other display will be used. Reconnect it, or choose a different display below.";
                break;
            case MatchKind.Fallback:
                OutputChip = "OUTPUT: CONFIRM DISPLAY";
                OutputAlert = $"Windows no longer reports the saved LED display ID, but one display has the same name, size and position " +
                              $"(\"{_output.Display!.FriendlyName}\", {_output.Display.Resolution}). Confirm it is the LED display before output can be used.";
                break;
            default:
                OutputChip = _outputRunning && _blackout ? "OUTPUT: BLACKOUT"
                    : _outputRunning ? (_testLabel == null ? "OUTPUT: LIVE" : "OUTPUT: LIVE — TEST: " + _testLabel) : "OUTPUT: STOPPED";
                OutputAlert = null;
                break;
        }

        if (_settings.OperatorDisplay != null && !_operator.IsAvailable)
            OperatorNote = $"The selected Operator display \"{_settings.OperatorDisplay.Describe()}\" is not connected.";
        else
            OperatorNote = null;
    }

    private void SetOutput(DisplayInfo d)
    {
        var warnings = SelectionPolicy.CheckLedOutput(d, _current,
            _operator.IsAvailable ? _operator.Display : null, _operatorWindowMonitor());
        if (warnings.Count > 0 && !_confirm(warnings, $"Use \"{d.FriendlyName}\" ({d.Resolution}) as the LED output?"))
            return;

        _settings.OutputDisplay = DisplayIdentity.From(d);
        _saveSettings();
        _log.Info($"LED output display selected: {Describe(d)}");
        Refresh();
    }

    private void SetOperator(DisplayInfo d)
    {
        var warnings = SelectionPolicy.CheckOperator(d, ConfirmedOutputDisplay ?? (_output.IsAvailable ? _output.Display : null));
        if (warnings.Count > 0 && !_confirm(warnings, $"Use \"{d.FriendlyName}\" as the Operator display?"))
            return;

        _settings.OperatorDisplay = DisplayIdentity.From(d);
        _saveSettings();
        _log.Info($"Operator display selected: {Describe(d)}");
        Refresh();
    }

    private void ConfirmFallback()
    {
        if (_output.Kind != MatchKind.Fallback || _output.Display is null) return;
        _settings.OutputDisplay = DisplayIdentity.From(_output.Display);
        _saveSettings();
        _log.Info($"LED output display confirmed after ID change: {Describe(_output.Display)}");
        Refresh();
    }

    private void ClearOutput()
    {
        _settings.OutputDisplay = null;
        _saveSettings();
        _log.Info("LED output display selection cleared.");
        Refresh();
    }

    private void IdentifyAll()
    {
        for (var i = 0; i < _current.Count; i++)
        {
            try { _identify(_current[i], i + 1); }
            catch (Exception ex) { _log.Warn("Could not show identify label.", ex); }
        }
    }

    /// <summary>Keeps the remembered geometry current when the same display (by ID) changed resolution or position.</summary>
    private void RefreshRememberedGeometry()
    {
        var changed = false;
        if (_output.Kind == MatchKind.Exact && _settings.OutputDisplay!.DiffersFrom(_output.Display!))
        {
            _settings.OutputDisplay = DisplayIdentity.From(_output.Display!);
            changed = true;
        }
        if (_operator.Kind == MatchKind.Exact && _settings.OperatorDisplay!.DiffersFrom(_operator.Display!))
        {
            _settings.OperatorDisplay = DisplayIdentity.From(_operator.Display!);
            changed = true;
        }
        if (changed) _saveSettings();
    }

    private void LogIfChanged(IReadOnlyList<DisplayInfo> ordered)
    {
        var signature = string.Join("|", ordered.Select(d => $"{d.DevicePath}@{d.X},{d.Y},{d.Width}x{d.Height},{d.DpiX}"));
        if (signature == _lastSignature) return;
        _lastSignature = signature;
        _log.Info($"Displays detected: {ordered.Count}");
        for (var i = 0; i < ordered.Count; i++)
            _log.Info($"  Display {i + 1}: {Describe(ordered[i])}");
    }

    private void LogOutputTransition()
    {
        if (_lastOutputKind == _output.Kind) return;
        var previous = _lastOutputKind;
        _lastOutputKind = _output.Kind;
        if (previous is MatchKind.Exact or MatchKind.Fallback && _output.Kind == MatchKind.NotFound)
            _log.Warn($"LED output display lost: {_settings.OutputDisplay?.Describe()}");
        else if (previous == MatchKind.NotFound && _output.IsAvailable)
            _log.Info($"LED output display is available again: {Describe(_output.Display!)}");
        else if (_output.Kind == MatchKind.NotFound)
            _log.Warn($"Saved LED output display not found at startup: {_settings.OutputDisplay?.Describe()}");
        else if (_output.Kind == MatchKind.Fallback)
            _log.Warn($"LED output display matched only by name/size/position: {Describe(_output.Display!)}");
    }

    private static bool Same(DisplayInfo a, DisplayInfo b) => a.Handle == b.Handle;

    private static string Describe(DisplayInfo d) =>
        $"{d.FriendlyName} {d.Resolution} at ({d.X},{d.Y}) {d.ScalePercent}% [{d.DeviceName}]{(d.IsPrimary ? " primary" : "")} id={d.DevicePath}";

    private void OnSystemDisplayChange(object? sender, EventArgs e)
    {
        // Raised on a non-UI thread; DispatcherTimer must be touched from the UI thread.
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { _debounce.Stop(); _debounce.Start(); });
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnSystemDisplayChange;
        _debounce.Stop();
    }
}
