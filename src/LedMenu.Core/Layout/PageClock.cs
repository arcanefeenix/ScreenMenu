namespace LedMenu.Core.Layout;

/// <summary>
/// Decides which page of a multi-page menu is showing, from elapsed time alone (so there is no drift and it
/// is easy to test). Each key (a screen, or a previewed menu) has its own start time.
/// - A one-page menu always shows page 0 and never rotates.
/// - Otherwise page = floor(elapsed / period) mod pageCount: page 1 for a full period first, then 2, ... then back to 1.
/// - If the page count of a key changes (content was edited), rotation restarts at page 1 so the new first page gets a full period.
/// </summary>
public sealed class PageClock
{
    public const double MinSeconds = 2;
    public const double MaxSeconds = 120;

    private readonly Dictionary<Guid, (TimeSpan Start, int Count)> _state = new();

    /// <summary>The page duration, clamped to a sensible range (a bad value in a file cannot freeze or flicker the wall).</summary>
    public static TimeSpan Period(int seconds) =>
        TimeSpan.FromSeconds(Math.Clamp(seconds, MinSeconds, MaxSeconds));

    public void Reset(Guid key, TimeSpan now) => _state.Remove(key);
    public void ResetAll() => _state.Clear();

    public int PageFor(Guid key, int pageCount, TimeSpan period, TimeSpan now)
    {
        if (pageCount <= 1)
        {
            _state[key] = (now, Math.Max(1, pageCount));
            return 0;
        }

        if (!_state.TryGetValue(key, out var s) || s.Count != pageCount)
        {
            s = (now, pageCount);
            _state[key] = s;
        }

        var elapsed = now - s.Start;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        var step = (long)Math.Floor(elapsed.TotalSeconds / period.TotalSeconds);
        return (int)(step % pageCount);
    }

    /// <summary>Seconds until the page changes, for a countdown or for choosing a timer interval.</summary>
    public double SecondsUntilNext(Guid key, int pageCount, TimeSpan period, TimeSpan now)
    {
        if (pageCount <= 1 || !_state.TryGetValue(key, out var s)) return double.PositiveInfinity;
        var elapsed = Math.Max(0, (now - s.Start).TotalSeconds);
        return period.TotalSeconds - (elapsed % period.TotalSeconds);
    }
}
