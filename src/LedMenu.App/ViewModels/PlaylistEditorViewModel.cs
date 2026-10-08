using System.Collections.ObjectModel;
using LedMenu.App.Infrastructure;
using LedMenu.App.Output;
using LedMenu.Core.Assets;
using LedMenu.Core.Screens;
using LedMenu.Persistence;
using LedMenu.Rendering;

namespace LedMenu.App.ViewModels;

/// <summary>What the playlist editors need from the rest of the program, so they can be tested without a window, a disk or a decoder.</summary>
public sealed class VideoEditingServices
{
    /// <summary>Shows the file-open dialog and returns the chosen files (empty if cancelled).</summary>
    public required Func<IReadOnlyList<string>> PickFiles { get; init; }

    /// <summary>Copies a video into the media folder if Windows can play it.</summary>
    public required Func<string, CancellationToken, Task<MediaImportResult>> Import { get; init; }

    public required Func<string, bool> MediaExists { get; init; }

    /// <summary>Asks a yes/no question (text, title).</summary>
    public required Func<string, string, bool> Confirm { get; init; }

    /// <summary>Deletes media files that no playlist uses and returns their names.</summary>
    public required Func<IReadOnlySet<string>, IReadOnlyList<string>> RemoveUnused { get; init; }

    /// <summary>Previous / Next / Pause on the playing videos. Null when nothing can play (tests of the editing alone).</summary>
    public IVideoTransport? Transport { get; init; }
}

/// <summary>One row of a playlist.</summary>
public sealed class VideoItemViewModel : ObservableObject
{
    private readonly PlaylistEditorViewModel _owner;
    private string? _warning;

    public VideoItemViewModel(VideoItem model, PlaylistEditorViewModel owner)
    {
        Model = model;
        _owner = owner;
        MoveUpCommand = new RelayCommand(() => _owner.Move(this, -1), () => _owner.CanMove(this, -1));
        MoveDownCommand = new RelayCommand(() => _owner.Move(this, +1), () => _owner.CanMove(this, +1));
        RemoveCommand = new RelayCommand(() => _owner.Remove(this));
    }

    public VideoItem Model { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand RemoveCommand { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Model.DisplayName) ? Model.FileName : Model.DisplayName;

    /// <summary>For example "168×672 · 0:38".</summary>
    public string Detail
    {
        get
        {
            var size = Model.Width > 0 && Model.Height > 0 ? $"{Model.Width}×{Model.Height}" : "size unknown";
            return $"{size} · {MediaRules.FormatDuration(Model.DurationSeconds)}";
        }
    }

    public bool Enabled
    {
        get => Model.Enabled;
        set { if (Model.Enabled != value) { Model.Enabled = value; OnPropertyChanged(); _owner.Edited($"video {(value ? "switched on" : "switched off")}: {DisplayName}"); } }
    }

    /// <summary>A problem with this video that the operator should know about, or null.</summary>
    public string? Warning { get => _warning; set { if (Set(ref _warning, value)) OnPropertyChanged(nameof(HasWarning)); } }
    public bool HasWarning => !string.IsNullOrEmpty(_warning);

    public void RaiseMoveStateChanged()
    {
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>
/// Edits one video screen's playlist: import videos, put them in order, switch them on and off, remove them, choose looping and Fit or Fill,
/// and control what is playing (Previous, Pause, Next). Every change is saved at once and takes effect on the wall without interrupting the video that is playing.
/// </summary>
public sealed class PlaylistEditorViewModel : ObservableObject
{
    private readonly ScreenItemViewModel _screen;
    private readonly VideoEditingServices _services;
    private string? _message;
    private string _importStatus = "";
    private bool _isImporting;
    private string _liveStatus = "";
    private bool _hasLivePlayer;
    private bool _paused;

    public PlaylistEditorViewModel(ScreenItemViewModel screen, VideoEditingServices services)
    {
        _screen = screen;
        _services = services;
        ImportCommand = new RelayCommand(() => _ = ImportAsync(), () => !_isImporting);
        DismissMessageCommand = new RelayCommand(() => Message = null);
        PreviousCommand = new RelayCommand(() => _services.Transport?.Previous(_screen.Model.Id), () => _hasLivePlayer);
        NextCommand = new RelayCommand(() => _services.Transport?.Next(_screen.Model.Id), () => _hasLivePlayer);
        PlayPauseCommand = new RelayCommand(() => _services.Transport?.TogglePause(_screen.Model.Id), () => _hasLivePlayer);
        if (_services.Transport != null) _services.Transport.StatusChanged += OnTransportStatusChanged;
        Rebuild();
        RefreshLiveStatus();
    }

    private VideoPlaylist Playlist => _screen.Model.Playlist;

    public ObservableCollection<VideoItemViewModel> Items { get; } = new();
    public RelayCommand ImportCommand { get; }
    public RelayCommand DismissMessageCommand { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand PlayPauseCommand { get; }

    public bool HasNoVideos => Items.Count == 0;

    public bool Loop
    {
        get => Playlist.Loop;
        set { if (Playlist.Loop != value) { Playlist.Loop = value; OnPropertyChanged(); Edited($"looping {(value ? "on" : "off")}"); } }
    }

    public bool IsFit
    {
        get => Playlist.Fit == VideoFit.Fit;
        set { if (value && Playlist.Fit != VideoFit.Fit) SetFit(VideoFit.Fit); }
    }

    public bool IsFill
    {
        get => Playlist.Fit == VideoFit.Fill;
        set { if (value && Playlist.Fit != VideoFit.Fill) SetFit(VideoFit.Fill); }
    }

    private void SetFit(VideoFit fit)
    {
        Playlist.Fit = fit;
        OnPropertyChanged(nameof(IsFit));
        OnPropertyChanged(nameof(IsFill));
        RefreshWarnings();
        Edited($"fit set to {fit}");
    }

    public string? Message
    {
        get => _message;
        private set { if (Set(ref _message, value)) OnPropertyChanged(nameof(HasMessage)); }
    }
    public bool HasMessage => !string.IsNullOrEmpty(_message);

    /// <summary>"Importing 2 of 3: name.mp4..." while an import runs.</summary>
    public string ImportStatus { get => _importStatus; private set => Set(ref _importStatus, value); }

    public bool IsImporting
    {
        get => _isImporting;
        private set { if (Set(ref _isImporting, value)) ImportCommand.RaiseCanExecuteChanged(); }
    }

    /// <summary>What is playing right now, in words.</summary>
    public string LiveStatus { get => _liveStatus; private set => Set(ref _liveStatus, value); }

    public string PlayPauseText => _paused ? "▶ Resume" : "⏸ Pause";

    // ---- importing ----

    /// <summary>Asks for video files and adds each one that Windows can play. Files that cannot be used are reported by name and the rest are still added.</summary>
    public async Task ImportAsync()
    {
        IReadOnlyList<string> files;
        try { files = _services.PickFiles(); }
        catch (Exception ex) { Message = "The file dialog could not be opened: " + ex.Message; return; }
        if (files.Count == 0) return;
        await ImportFilesAsync(files);
    }

    public async Task ImportFilesAsync(IReadOnlyList<string> files)
    {
        IsImporting = true;
        Message = null;
        var added = new List<string>();
        var problems = new List<string>();
        try
        {
            for (var i = 0; i < files.Count; i++)
            {
                var name = System.IO.Path.GetFileName(files[i]);
                ImportStatus = $"Importing {i + 1} of {files.Count}: {name} …";
                try
                {
                    var result = await _services.Import(files[i], CancellationToken.None);
                    Playlist.Items.Add(result.Item);
                    added.Add(name);
                }
                catch (MediaImportException ex) { problems.Add($"{name}: {ex.Message}"); }
                catch (Exception ex) { problems.Add($"{name}: {ex.Message}"); }
            }
        }
        finally
        {
            ImportStatus = "";
            IsImporting = false;
        }

        if (added.Count > 0)
        {
            Rebuild();
            Edited($"{added.Count} video(s) added: {string.Join(", ", added)}");
        }
        var lines = new List<string>();
        if (added.Count > 0) lines.Add($"Added {added.Count} video{(added.Count == 1 ? "" : "s")}.");
        lines.AddRange(problems.Select(p => "Not added — " + p));
        Message = lines.Count == 0 ? null : string.Join("\n", lines);
    }

    // ---- editing the list ----

    internal bool CanMove(VideoItemViewModel item, int by)
    {
        var i = Items.IndexOf(item);
        return i >= 0 && i + by >= 0 && i + by < Items.Count;
    }

    internal void Move(VideoItemViewModel item, int by)
    {
        var i = Items.IndexOf(item);
        if (i < 0 || i + by < 0 || i + by >= Items.Count) return;
        Playlist.Items.RemoveAt(i);
        Playlist.Items.Insert(i + by, item.Model);
        Items.Move(i, i + by);
        foreach (var it in Items) it.RaiseMoveStateChanged();
        Edited($"video moved: {item.DisplayName}");
    }

    internal void Remove(VideoItemViewModel item)
    {
        if (!Playlist.Items.Remove(item.Model)) return;
        Items.Remove(item);
        foreach (var it in Items) it.RaiseMoveStateChanged();
        OnPropertyChanged(nameof(HasNoVideos));
        Edited($"video removed from the playlist: {item.DisplayName}");      // the file stays in the media folder; clean up unused files separately
    }

    internal void Edited(string what) => _screen.PlaylistEdited(what);

    // ---- warnings and live status ----

    private void Rebuild()
    {
        Items.Clear();
        foreach (var v in Playlist.Items) Items.Add(new VideoItemViewModel(v, this));
        foreach (var it in Items) it.RaiseMoveStateChanged();
        OnPropertyChanged(nameof(HasNoVideos));
        OnPropertyChanged(nameof(Loop));
        OnPropertyChanged(nameof(IsFit));
        OnPropertyChanged(nameof(IsFill));
        RefreshWarnings();
    }

    /// <summary>Recomputes each video's warning (file missing, wrong size for the screen). Called when the screen's size or the media change.</summary>
    public void RefreshWarnings()
    {
        var sw = _screen.Model.Width; var sh = _screen.Model.Height;
        foreach (var it in Items)
        {
            var v = it.Model;
            if (!_services.MediaExists(v.FileName)) it.Warning = "This video's file is missing from the media folder, so it will be skipped.";
            else if (v.Width > 0 && v.Height > 0 && sw > 0 && sh > 0 && (v.Width != sw || v.Height != sh))
                it.Warning = $"Shape differs from the screen ({v.Width}×{v.Height} on {sw}×{sh}): it will be {(Playlist.Fit == VideoFit.Fill ? "scaled and cropped" : "scaled to fit")}, not shown 1:1.";
            else it.Warning = null;
        }
    }

    private void OnTransportStatusChanged(Guid id)
    {
        if (id == _screen.Model.Id) RefreshLiveStatus();
    }

    public void RefreshLiveStatus()
    {
        var s = _services.Transport?.StatusOf(_screen.Model.Id);
        _paused = s?.State == Rendering.VideoPlayerState.Paused;
        _hasLivePlayer = s != null && s.State != Rendering.VideoPlayerState.Idle;
        LiveStatus = s == null || s.State == Rendering.VideoPlayerState.Idle
            ? "Not playing. Videos play while the LED output is running, or use Preview videos in the output panel."
            : s.State switch
            {
                Rendering.VideoPlayerState.Opening => "Opening the video …",
                Rendering.VideoPlayerState.Playing => $"Playing {s.Position} of {s.PlayableCount}: {s.CurrentName}",
                Rendering.VideoPlayerState.Paused => $"Paused on {s.Position} of {s.PlayableCount}: {s.CurrentName}",
                Rendering.VideoPlayerState.Finished => "Finished. The playlist is not set to loop; press Previous or Next, or restart output, to play again.",
                _ => "Nothing on this screen can be played.",
            };
        if (s?.Problem is { Length: > 0 } p) LiveStatus += "\n⚠ " + p;
        OnPropertyChanged(nameof(PlayPauseText));
        PreviousCommand.RaiseCanExecuteChanged();
        NextCommand.RaiseCanExecuteChanged();
        PlayPauseCommand.RaiseCanExecuteChanged();
    }

    /// <summary>The playlist or the media folder changed from outside this editor (a file was cleaned up, the screen was resized).</summary>
    public void Refresh()
    {
        RefreshWarnings();
        RefreshLiveStatus();
    }
}
