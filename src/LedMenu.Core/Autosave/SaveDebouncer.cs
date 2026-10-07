namespace LedMenu.Core.Autosave;

/// <summary>Runs an action once after a delay. The real implementation uses the UI timer; tests use a fake that they advance by hand.</summary>
public interface IScheduler
{
    IDisposable ScheduleOnce(TimeSpan delay, Action callback);
}

/// <summary>
/// Autosave policy for typed text. Typing never writes on every keystroke: each change restarts a short timer and only the
/// last change in a burst is saved (and announced). Discrete actions (Sold Out, Hide, add, delete, move) save at once and
/// replace anything still waiting for the same key. <see cref="FlushAll"/> runs everything still waiting, for use when focus
/// leaves a box, when switching menus, and on exit, so nothing typed is ever lost.
/// </summary>
public sealed class SaveDebouncer
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(500);

    private sealed class Pending
    {
        public required Action Action { get; init; }
        public required IDisposable Timer { get; init; }
    }

    private readonly IScheduler _scheduler;
    private readonly TimeSpan _delay;
    private readonly Dictionary<Guid, Pending> _pending = new();

    public SaveDebouncer(IScheduler scheduler, TimeSpan? delay = null)
    {
        _scheduler = scheduler;
        _delay = delay ?? DefaultDelay;
    }

    public int PendingCount => _pending.Count;
    public bool HasPending(Guid key) => _pending.ContainsKey(key);

    /// <summary>Schedules <paramref name="action"/> after the delay (or <paramref name="delay"/>), replacing any earlier request for the same key.</summary>
    public void Request(Guid key, Action action, TimeSpan? delay = null)
    {
        Cancel(key);
        var timer = _scheduler.ScheduleOnce(delay ?? _delay, () => Run(key));
        _pending[key] = new Pending { Action = action, Timer = timer };
    }

    /// <summary>Runs <paramref name="action"/> now and drops anything still waiting for the same key.</summary>
    public void Immediate(Guid key, Action action)
    {
        Cancel(key);
        action();
    }

    /// <summary>Runs one key's waiting action now, if there is one.</summary>
    public void Flush(Guid key) => Run(key);

    /// <summary>Runs every waiting action now.</summary>
    public void FlushAll()
    {
        foreach (var key in _pending.Keys.ToList()) Run(key);
    }

    private void Run(Guid key)
    {
        if (!_pending.Remove(key, out var p)) return;
        p.Timer.Dispose();
        p.Action();
    }

    private void Cancel(Guid key)
    {
        if (_pending.Remove(key, out var p)) p.Timer.Dispose();
    }
}
