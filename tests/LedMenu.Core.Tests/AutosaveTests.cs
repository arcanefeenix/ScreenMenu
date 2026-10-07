using LedMenu.Core.Autosave;

namespace LedMenu.Core.Tests;

internal sealed class FakeScheduler : IScheduler
{
    private sealed class Entry : IDisposable
    {
        public required TimeSpan Due { get; init; }
        public required Action Callback { get; init; }
        public bool Cancelled { get; set; }
        public void Dispose() => Cancelled = true;
    }

    private readonly List<Entry> _entries = new();
    public TimeSpan Now { get; private set; }

    public IDisposable ScheduleOnce(TimeSpan delay, Action callback)
    {
        var e = new Entry { Due = Now + delay, Callback = callback };
        _entries.Add(e);
        return e;
    }

    public void Advance(TimeSpan by)
    {
        Now += by;
        foreach (var e in _entries.Where(e => !e.Cancelled && e.Due <= Now).ToList())
        {
            e.Cancelled = true;
            e.Callback();
        }
    }

    public int ActiveTimers => _entries.Count(e => !e.Cancelled);
}

public class SaveDebouncerTests
{
    private static readonly TimeSpan Ms500 = TimeSpan.FromMilliseconds(500);
    private readonly FakeScheduler _clock = new();
    private readonly Guid _a = Guid.NewGuid();
    private readonly Guid _b = Guid.NewGuid();

    private SaveDebouncer Make() => new(_clock);

    [Fact]
    public void A_request_runs_once_after_the_delay_not_before()
    {
        var d = Make();
        var runs = 0;
        d.Request(_a, () => runs++);
        _clock.Advance(TimeSpan.FromMilliseconds(499));
        Assert.Equal(0, runs);
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, runs);
        _clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1, runs);
        Assert.Equal(0, d.PendingCount);
    }

    [Fact]
    public void A_burst_of_keystrokes_causes_exactly_one_save_with_the_last_value()
    {
        var d = Make();
        var saved = new List<string>();
        var text = "";
        foreach (var ch in "$14.50")
        {
            text += ch;
            var snapshot = text;
            d.Request(_a, () => saved.Add(snapshot));
            _clock.Advance(TimeSpan.FromMilliseconds(120));       // typing faster than the delay
        }
        Assert.Empty(saved);
        _clock.Advance(Ms500);
        Assert.Equal(new[] { "$14.50" }, saved);
        Assert.Equal(1, d.PendingCount + 1);                       // nothing left waiting
    }

    [Fact]
    public void A_pause_in_typing_saves_and_further_typing_saves_again()
    {
        var d = Make();
        var saved = 0;
        d.Request(_a, () => saved++);
        _clock.Advance(Ms500);
        d.Request(_a, () => saved++);
        _clock.Advance(Ms500);
        Assert.Equal(2, saved);
    }

    [Fact]
    public void Keys_are_independent()
    {
        var d = Make();
        var ran = new List<string>();
        d.Request(_a, () => ran.Add("a"));
        _clock.Advance(TimeSpan.FromMilliseconds(300));
        d.Request(_b, () => ran.Add("b"));
        _clock.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal(new[] { "a" }, ran);
        _clock.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Equal(new[] { "a", "b" }, ran);
    }

    [Fact]
    public void A_discrete_action_runs_immediately_and_replaces_a_waiting_request()
    {
        var d = Make();
        var log = new List<string>();
        d.Request(_a, () => log.Add("typed"));
        d.Immediate(_a, () => log.Add("immediate"));
        Assert.Equal(new[] { "immediate" }, log);
        _clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "immediate" }, log);                  // the waiting request was superseded, not run later
        Assert.Equal(0, _clock.ActiveTimers);
    }

    [Fact]
    public void Flush_runs_a_waiting_request_now_and_cancels_its_timer()
    {
        var d = Make();
        var runs = 0;
        d.Request(_a, () => runs++);
        d.Flush(_a);
        Assert.Equal(1, runs);
        _clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, runs);
        d.Flush(_a);                                               // nothing waiting: harmless
        Assert.Equal(1, runs);
    }

    [Fact]
    public void FlushAll_runs_everything_waiting_for_exit()
    {
        var d = Make();
        var ran = new List<string>();
        d.Request(_a, () => ran.Add("a"));
        d.Request(_b, () => ran.Add("b"));
        Assert.Equal(2, d.PendingCount);
        d.FlushAll();
        Assert.Equal(new[] { "a", "b" }.OrderBy(x => x), ran.OrderBy(x => x));
        Assert.Equal(0, d.PendingCount);
        Assert.Equal(0, _clock.ActiveTimers);
    }

    [Fact]
    public void An_action_that_requests_again_while_running_is_kept()
    {
        var d = Make();
        var runs = 0;
        d.Request(_a, () => { runs++; if (runs == 1) d.Request(_a, () => runs += 10); });
        _clock.Advance(Ms500);
        Assert.Equal(1, runs);
        Assert.True(d.HasPending(_a));
        _clock.Advance(Ms500);
        Assert.Equal(11, runs);
    }

    [Fact]
    public void Default_delay_is_half_a_second() => Assert.Equal(Ms500, SaveDebouncer.DefaultDelay);
}
