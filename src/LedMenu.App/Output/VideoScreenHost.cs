using System.Windows.Threading;
using LedMenu.Core.Calibration;
using LedMenu.Core.Logging;
using LedMenu.Core.Screens;
using LedMenu.Rendering;

namespace LedMenu.App.Output;

/// <summary>What the operator is told about one video screen.</summary>
public sealed record VideoScreenStatus(VideoPlayerState State, string? CurrentName, int Position, int PlayableCount, string? Problem);

/// <summary>The operator's controls over the videos that are playing (Previous, Next, Pause) and what they may show.</summary>
public interface IVideoTransport
{
    VideoScreenStatus? StatusOf(Guid screenId);
    void Next(Guid screenId);
    void Previous(Guid screenId);
    /// <summary>Pauses a playing video screen, or lets a paused one carry on.</summary>
    void TogglePause(Guid screenId);
    /// <summary>The status of a screen changed (state, current video or a problem).</summary>
    event Action<Guid>? StatusChanged;
}

/// <summary>
/// Keeps one <see cref="VideoScreenPlayer"/> for every video screen that is being drawn: creates it when a screen turns to video,
/// rebuilds it if the screen is resized, hands it edits to the playlist as they happen, and disposes it when the screen goes away.
/// Videos only play while LED output is running. Each screen has its own player, so one screen's bad video, slow decoder
/// or empty playlist cannot affect another screen. UI thread only.
/// </summary>
public sealed class VideoScreenHost : IDisposable, IVideoTransport
{
    private sealed class Entry
    {
        public required VideoScreenPlayer Player { get; init; }
        public required CalibrationScreen Screen { get; set; }
    }

    private readonly Dispatcher _dispatcher;
    private readonly Func<string, string?> _resolve;
    private readonly IAppLog _log;
    private readonly Dictionary<Guid, Entry> _entries = new();
    private readonly Dictionary<Guid, VideoPlaylist> _playlists = new();
    private bool _running;
    private string _problemText = "";

    public VideoScreenHost(Dispatcher dispatcher, Func<string, string?> resolveMediaPath, IAppLog log)
    {
        _dispatcher = dispatcher;
        _resolve = resolveMediaPath;
        _log = log;
    }

    /// <summary>A video screen has a new picture (the id is the screen's id).</summary>
    public event Action<Guid>? PictureUpdated;

    /// <summary>The set of things to tell the operator changed (a video was skipped, a screen has nothing to play).</summary>
    public event Action? ProblemsChanged;

    public event Action<Guid>? StatusChanged;

    public int PlayerCount => _entries.Count;

    /// <summary>One line per screen that has something to report, such as a skipped video. Empty when all is well.</summary>
    public IReadOnlyList<string> Problems { get; private set; } = Array.Empty<string>();

    /// <summary>The latest picture of a video screen (exactly the screen's size), or null if the screen has no player.</summary>
    public PixelBuffer? PictureFor(Guid screenId) => _entries.TryGetValue(screenId, out var e) ? e.Player.Frame : null;

    public VideoPlayerState? StateOf(Guid screenId) => _entries.TryGetValue(screenId, out var e) ? e.Player.State : null;

    public VideoScreenStatus? StatusOf(Guid screenId)
    {
        if (!_entries.TryGetValue(screenId, out var e)) return null;
        var p = e.Player;
        var playable = _playlists.TryGetValue(screenId, out var list) ? list.Playable.ToList() : new List<VideoItem>();
        var current = p.Current;
        var at = current == null ? 0 : playable.FindIndex(i => i.Id == current.Id) + 1;
        var name = current == null ? null : string.IsNullOrWhiteSpace(current.DisplayName) ? current.FileName : current.DisplayName;
        return new VideoScreenStatus(p.State, name, at, playable.Count, p.Problem);
    }

    public void Next(Guid screenId) { if (_entries.TryGetValue(screenId, out var e)) e.Player.Next(); }

    public void Previous(Guid screenId) { if (_entries.TryGetValue(screenId, out var e)) e.Player.Previous(); }

    public void TogglePause(Guid screenId)
    {
        if (!_entries.TryGetValue(screenId, out var e)) return;
        if (e.Player.State == VideoPlayerState.Paused) e.Player.Play();
        else if (e.Player.State is VideoPlayerState.Playing or VideoPlayerState.Opening) e.Player.Pause();
        StatusChanged?.Invoke(screenId);
    }

    /// <summary>
    /// Makes the players match the screens that are being drawn. <paramref name="models"/> carry the playlists;
    /// <paramref name="drawn"/> says which of them are drawn (enabled and valid) and at what size.
    /// </summary>
    public void Sync(IReadOnlyList<Screen> models, IReadOnlyList<CalibrationScreen> drawn)
    {
        var wanted = drawn.Where(d => d.IsVideo).ToDictionary(d => d.Id);

        foreach (var id in _entries.Keys.Where(id => !wanted.ContainsKey(id)).ToList()) Remove(id);

        foreach (var (id, d) in wanted)
        {
            var model = models.FirstOrDefault(m => m.Id == id);
            if (model == null) { Remove(id); continue; }

            if (_entries.TryGetValue(id, out var existing) && (existing.Screen.Width != d.Width || existing.Screen.Height != d.Height))
                Remove(id);                                  // a resized screen needs a picture of the new size

            if (!_entries.TryGetValue(id, out var entry))
            {
                var player = new VideoScreenPlayer(_dispatcher, d.Width, d.Height, _resolve, _log);
                player.FrameUpdated += () => PictureUpdated?.Invoke(id);
                player.StateChanged += () => { RefreshProblems(); StatusChanged?.Invoke(id); };
                player.ItemChanged += () => StatusChanged?.Invoke(id);
                entry = new Entry { Player = player, Screen = d };
                _entries[id] = entry;
                _log.Info($"Video screen ready: \"{d.Name}\" {d.Width}x{d.Height}.");
            }

            entry.Screen = d;
            _playlists[id] = model.Playlist;
            entry.Player.SetPlaylist(model.Playlist);       // live: edits never interrupt the video that is playing
            if (_running && entry.Player.State == VideoPlayerState.Idle) entry.Player.Play();
        }
        RefreshProblems();
        foreach (var id in _entries.Keys) StatusChanged?.Invoke(id);
    }

    /// <summary>Videos play while output runs and stop (screens go black) when it stops.</summary>
    public void SetRunning(bool running)
    {
        if (_running == running) return;
        _running = running;
        foreach (var e in _entries.Values)
        {
            if (running) { if (e.Player.State == VideoPlayerState.Idle) e.Player.Play(); }
            else e.Player.Stop();
        }
        RefreshProblems();
    }

    public void Dispose()
    {
        foreach (var id in _entries.Keys.ToList()) Remove(id);
    }

    private void Remove(Guid id)
    {
        _playlists.Remove(id);
        if (!_entries.Remove(id, out var e)) return;
        try { e.Player.Dispose(); } catch (Exception ex) { _log.Warn("A video player did not close cleanly.", ex); }
    }

    private void RefreshProblems()
    {
        var list = new List<string>();
        foreach (var e in _entries.Values)
        {
            var label = string.IsNullOrWhiteSpace(e.Screen.Name) ? $"Screen {e.Screen.Number}" : $"Screen {e.Screen.Number} \"{e.Screen.Name}\"";
            if (!string.IsNullOrWhiteSpace(e.Player.Problem) && _running) list.Add($"{label}: {e.Player.Problem}");
        }
        var text = string.Join("\n", list);
        if (text == _problemText) return;
        _problemText = text;
        Problems = list;
        ProblemsChanged?.Invoke();
    }
}
