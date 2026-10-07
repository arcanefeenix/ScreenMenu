namespace LedMenu.Core.Logging;

/// <summary>
/// Decides whether an unexpected error may open a dialog. Every error is still logged, but a failure that repeats
/// on a timer (every quarter second, say) must not stack up dialogs over a running event: while one dialog is open no
/// other opens, and the same message is not shown again for a while after it was dismissed.
/// </summary>
public sealed class ErrorDialogThrottle
{
    private readonly TimeSpan _quiet;
    private readonly Dictionary<string, DateTime> _lastShown = new();
    private bool _open;

    public ErrorDialogThrottle(TimeSpan? quietPeriod = null) => _quiet = quietPeriod ?? TimeSpan.FromMinutes(2);

    /// <summary>True if a dialog may be shown now. Call <see cref="Dismissed"/> when it closes.</summary>
    public bool TryShow(string message, DateTime now)
    {
        if (_open) return false;
        if (_lastShown.TryGetValue(message, out var last) && now - last < _quiet) return false;
        _open = true;
        _lastShown[message] = now;
        return true;
    }

    public void Dismissed(DateTime now)
    {
        _open = false;
        foreach (var k in _lastShown.Keys.ToList()) _lastShown[k] = now;   // the quiet period runs from dismissal
    }
}
