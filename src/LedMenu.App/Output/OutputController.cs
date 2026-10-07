using System.Windows;
using LedMenu.App.Display;
using LedMenu.Core.Display;
using LedMenu.Core.Logging;
using static LedMenu.App.Display.NativeMethods;

namespace LedMenu.App.Output;

/// <summary>
/// Owns the single LED output window. Starting and stopping only creates and destroys that window;
/// it never touches settings, screens or menus. UI thread only.
/// </summary>
public sealed class OutputController : IDisposable
{
    private readonly IAppLog _log;
    private OutputWindow? _window;
    private DisplayInfo? _display;

    public OutputController(IAppLog log) => _log = log;

    public bool IsRunning => _window != null;
    public DisplayInfo? Display => _display;
    public OutputWindow? Window => _window;
    public OutputGeometryReport? LastReport { get; private set; }

    /// <summary>Raised after the running state changes. The string is why it changed (for stops).</summary>
    public event Action<bool, string?>? StateChanged;

    /// <summary>Raised when the operator asks to stop from the output window itself.</summary>
    public event Action? StopRequested;

    public bool Start(DisplayInfo display)
    {
        if (_window != null) return true;
        try
        {
            var window = new OutputWindow(display, _log);
            window.StopRequested += () => StopRequested?.Invoke();
            window.Closed += OnWindowClosed;
            _window = window;
            _display = display;
            window.Show();           // ShowActivated=false: the operator keeps keyboard focus
            window.Place();

            var report = window.BuildReport();
            LastReport = report;
            LogReport("Output started", display, report, window);
            StateChanged?.Invoke(true, null);
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("Output window could not be started.", ex);
            CloseWindow();
            return false;
        }
    }

    public void Stop(string reason)
    {
        if (_window == null) return;
        _log.Info($"Output stopping: {reason}");
        CloseWindow();
        StateChanged?.Invoke(false, reason);
    }

    /// <summary>Re-places the running window after the same display changed size or position.</summary>
    public void Reposition(DisplayInfo display)
    {
        if (_window == null) return;
        _display = display;
        _window.Place(display);
        var report = _window.BuildReport();
        LastReport = report;
        LogReport("Output repositioned", display, report, _window);
    }

    private void CloseWindow()
    {
        var w = _window;
        _window = null;
        _display = null;
        if (w == null) return;
        w.Closed -= OnWindowClosed;
        try { w.Close(); } catch (Exception ex) { _log.Warn("Output window close failed.", ex); }
    }

    // The window was closed by something other than Stop (for example Alt+F4 on the output window).
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_window == null) return;
        _window = null;
        _display = null;
        _log.Info("Output window was closed externally.");
        StateChanged?.Invoke(false, "The output window was closed.");
    }

    private void LogReport(string what, DisplayInfo display, OutputGeometryReport r, OutputWindow window)
    {
        _log.Info($"{what} on {display.FriendlyName} [{display.DeviceName}]. " +
                  $"Windows bounds {r.ExpectedBounds}; window {r.WindowRect}; client {r.ClientWidth}×{r.ClientHeight}; " +
                  $"WPF DPI {r.WpfDpiX:0.#}/{r.WpfDpiY:0.#}; monitor DPI {r.MonitorDpiX}/{r.MonitorDpiY}; " +
                  $"window active={window.IsActive}; agrees={r.Agrees}");
        foreach (var p in r.Problems) _log.Warn("Output geometry problem: " + p);
    }

    public void Dispose() => CloseWindow();
}
