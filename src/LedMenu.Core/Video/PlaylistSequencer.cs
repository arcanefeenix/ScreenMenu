using LedMenu.Core.Screens;

namespace LedMenu.Core.Video;

public enum SequencerStatus
{
    /// <summary>Nothing has been started yet.</summary>
    Idle,
    /// <summary>A video is current.</summary>
    Playing,
    /// <summary>The last video ended and the playlist does not loop.</summary>
    Finished,
    /// <summary>No video can play: none are enabled, or every enabled video has failed.</summary>
    NothingPlayable,
}

/// <summary>
/// Decides which video of a playlist plays now and which comes next. It knows nothing about decoders or time: the player tells it
/// what happened (ended, failed, the operator pressed Next) and it answers with the video to play, or null for none.
/// - Disabled videos and videos that failed are skipped. A failed video stays skipped until <see cref="ClearFailures"/>.
/// - At the end of the list, looping goes back to the first playable video; otherwise the playlist is Finished.
/// - If the playlist is edited while playing, the current video keeps playing if it is still there and enabled; otherwise
///   playback moves on to the next playable video after where it was.
/// </summary>
public sealed class PlaylistSequencer
{
    private List<VideoItem> _items = new();
    private bool _loop = true;
    private readonly HashSet<Guid> _failed = new();
    private int _index = -1;

    public SequencerStatus Status { get; private set; } = SequencerStatus.Idle;

    /// <summary>The video that should be playing now, or null.</summary>
    public VideoItem? Current => Status == SequencerStatus.Playing && _index >= 0 && _index < _items.Count ? _items[_index] : null;

    /// <summary>How many playable (enabled, not failed) videos there are right now.</summary>
    public int PlayableCount => _items.Count(CanPlay);

    public int FailedCount => _failed.Count;

    private bool CanPlay(VideoItem v) => v.Enabled && !_failed.Contains(v.Id);

    /// <summary>Replaces the list. Returns true if the video that should be playing changed (so the player must switch).</summary>
    public bool SetItems(IEnumerable<VideoItem> items, bool loop)
    {
        var before = Current;
        var oldIndex = _index;
        _loop = loop;
        _items = items.ToList();
        _failed.RemoveWhere(id => _items.All(i => i.Id != id));      // forget failures of videos that are gone

        if (Status is SequencerStatus.Idle) return false;
        if (Status == SequencerStatus.Finished && before == null) return false;   // a finished show does not restart because the list was edited

        if (before != null)
        {
            var at = _items.FindIndex(i => i.Id == before.Id);
            if (at >= 0 && CanPlay(_items[at])) { _index = at; return false; }   // still there: keep playing it, even if the list moved
        }

        // the current video was removed or switched off (or nothing was playing): move on from where it was
        var from = Math.Clamp(oldIndex, -1, _items.Count);
        var next = FindFrom(from < 0 ? 0 : from, wrap: _loop);
        return Apply(next) || before != null;
    }

    /// <summary>Begins at the first playable video.</summary>
    public VideoItem? Start()
    {
        Apply(FindFrom(0, wrap: false));
        return Current;
    }

    /// <summary>The current video reached its end normally.</summary>
    public VideoItem? Ended()
    {
        if (Status != SequencerStatus.Playing) return Current;
        Apply(FindAfter(_index, wrap: _loop));
        return Current;
    }

    /// <summary>The current video could not be played. It is skipped from now on.</summary>
    public VideoItem? Failed()
    {
        if (Status != SequencerStatus.Playing || Current is not { } bad) return Current;
        _failed.Add(bad.Id);
        Apply(FindAfter(_index, wrap: _loop));      // a failure moves straight on; it never stops a show that still has something to play
        return Current;
    }

    /// <summary>The operator asked for the next video. Past the last one it wraps only if looping.</summary>
    public VideoItem? Next()
    {
        if (Status is SequencerStatus.Idle) return null;
        var start = Status == SequencerStatus.Playing ? _index : _items.Count - 1;
        var found = FindAfter(start, wrap: _loop);
        if (found < 0) return Current;               // already on the last one and not looping: stay
        Apply(found);
        return Current;
    }

    /// <summary>The operator asked for the previous video. Before the first one it wraps only if looping.</summary>
    public VideoItem? Previous()
    {
        if (Status is SequencerStatus.Idle) return null;
        var start = Status == SequencerStatus.Playing ? _index : _items.Count;
        var found = FindBefore(start, wrap: _loop);
        if (found < 0) return Current;
        Apply(found);
        return Current;
    }

    /// <summary>What will play after the current video ends, so it can be opened early. Null if the playlist will be finished.</summary>
    public VideoItem? PeekNext()
    {
        if (Status != SequencerStatus.Playing) return null;
        var i = FindAfter(_index, wrap: _loop);
        return i >= 0 ? _items[i] : null;
    }

    /// <summary>Gives every video another chance (used when the operator presses Play again).</summary>
    public void ClearFailures() => _failed.Clear();

    // ---- search helpers ----

    private bool Apply(int found)
    {
        var before = Current?.Id;
        if (found >= 0) { _index = found; Status = SequencerStatus.Playing; }
        else
        {
            _index = -1;
            Status = PlayableCount == 0 ? SequencerStatus.NothingPlayable : SequencerStatus.Finished;
        }
        return Current?.Id != before;
    }

    /// <summary>First playable index at or after <paramref name="start"/>.</summary>
    private int FindFrom(int start, bool wrap)
    {
        for (var i = start; i < _items.Count; i++) if (CanPlay(_items[i])) return i;
        if (wrap) for (var i = 0; i < Math.Min(start, _items.Count); i++) if (CanPlay(_items[i])) return i;
        return -1;
    }

    /// <summary>First playable index strictly after <paramref name="index"/>; with wrap, may come back to the same one.</summary>
    private int FindAfter(int index, bool wrap)
    {
        for (var i = index + 1; i < _items.Count; i++) if (CanPlay(_items[i])) return i;
        if (wrap) for (var i = 0; i <= Math.Min(index, _items.Count - 1); i++) if (CanPlay(_items[i])) return i;
        return -1;
    }

    private int FindBefore(int index, bool wrap)
    {
        for (var i = Math.Min(index, _items.Count) - 1; i >= 0; i--) if (CanPlay(_items[i])) return i;
        if (wrap) for (var i = _items.Count - 1; i >= Math.Max(index, 0) && i >= 0; i--) if (CanPlay(_items[i])) return i;
        return -1;
    }
}
