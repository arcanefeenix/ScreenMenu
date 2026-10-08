using System.Collections.ObjectModel;
using LedMenu.App.Infrastructure;
using LedMenu.Core.Calibration;
using LedMenu.Core.Logging;
using LedMenu.Core.Screens;

namespace LedMenu.App.ViewModels;

public sealed class ScreenItemViewModel : ObservableObject
{
    private readonly ScreensViewModel _owner;
    private IReadOnlyList<ScreenIssue> _issues = Array.Empty<ScreenIssue>();
    private int _number;

    public ScreenItemViewModel(Screen model, ScreensViewModel owner)
    {
        Model = model;
        _owner = owner;
        Playlist = owner.CreatePlaylistEditor(this);
        RemoveCommand = new RelayCommand(() => _owner.Remove(this));
        StandardSizeCommand = new RelayCommand(() =>
        {
            Width = ScreenDefaults.Wall2x2Width;
            Height = ScreenDefaults.Wall2x2Height;
        });
        NarrowSizeCommand = new RelayCommand(() =>
        {
            Width = ScreenDefaults.Wall1x2Width;
            Height = ScreenDefaults.Wall1x2Height;
        });
    }

    public Screen Model { get; }

    /// <summary>The video playlist editor for this screen, or null where video editing is not available.</summary>
    public PlaylistEditorViewModel? Playlist { get; }

    // ---- what the screen shows: a menu or a video playlist ----

    public bool IsMenuContent
    {
        get => !Model.ShowsVideo;
        set { if (value) SetContent(ScreenContentKind.Menu); }
    }

    public bool IsVideoContent
    {
        get => Model.ShowsVideo;
        set { if (value) SetContent(ScreenContentKind.Video); }
    }

    public bool ShowsMenuPicker => !Model.ShowsVideo;
    public bool ShowsVideoEditor => Model.ShowsVideo && Playlist != null;

    /// <summary>Switching between Menu and Video never loses anything: the menu choice and the playlist are both kept.</summary>
    private void SetContent(ScreenContentKind kind)
    {
        if (Model.ContentKind == kind) return;
        Model.ContentKind = kind;
        OnPropertyChanged(nameof(IsMenuContent));
        OnPropertyChanged(nameof(IsVideoContent));
        OnPropertyChanged(nameof(ShowsMenuPicker));
        OnPropertyChanged(nameof(ShowsVideoEditor));
        Playlist?.Refresh();
        _owner.ItemEdited(this);
    }

    internal void PlaylistEdited(string what) => _owner.PlaylistEdited(this, what);

    public RelayCommand RemoveCommand { get; }
    public RelayCommand StandardSizeCommand { get; }
    public RelayCommand NarrowSizeCommand { get; }

    public int Number { get => _number; set => Set(ref _number, value); }

    public string Name { get => Model.Name; set { if (Model.Name != value) { Model.Name = value ?? ""; OnPropertyChanged(); _owner.ItemEdited(this); } } }
    public int X { get => Model.X; set { if (Model.X != value) { Model.X = value; OnPropertyChanged(); _owner.ItemEdited(this); } } }
    public int Y { get => Model.Y; set { if (Model.Y != value) { Model.Y = value; OnPropertyChanged(); _owner.ItemEdited(this); } } }
    public int Width { get => Model.Width; set { if (Model.Width != value) { Model.Width = value; OnPropertyChanged(); _owner.ItemEdited(this); } } }
    public int Height { get => Model.Height; set { if (Model.Height != value) { Model.Height = value; OnPropertyChanged(); _owner.ItemEdited(this); } } }

    public bool Enabled
    {
        get => Model.Enabled;
        set { if (Model.Enabled != value) { Model.Enabled = value; OnPropertyChanged(); _owner.ItemEdited(this); } }
    }

    /// <summary>The menus this screen can show, for the drop-down.</summary>
    public IReadOnlyList<MenuChoice> MenuChoices => _owner.MenuChoices;

    /// <summary>The assigned menu's id, or <see cref="Guid.Empty"/> for none. An id whose menu is missing is kept as it is.</summary>
    public Guid AssignedMenuKey
    {
        get => Model.AssignedMenuId ?? Guid.Empty;
        set
        {
            Guid? v = value == Guid.Empty ? null : value;
            if (Model.AssignedMenuId == v) return;
            Model.AssignedMenuId = v;
            OnPropertyChanged();
            _owner.ItemEdited(this);
        }
    }

    public void RaiseMenusChanged()
    {
        OnPropertyChanged(nameof(MenuChoices));
        OnPropertyChanged(nameof(AssignedMenuKey));
    }

    public bool IsSelected => ReferenceEquals(_owner.Selected, this);
    public void Select() => _owner.Selected = this;
    public void RaiseSelectionChanged() => OnPropertyChanged(nameof(IsSelected));

    /// <summary>Used by dragging: sets all four values together without saving; <see cref="ScreensViewModel.CommitGeometry"/> saves.</summary>
    public void SetGeometryPreview(int x, int y, int w, int h)
    {
        Model.X = x; Model.Y = y; Model.Width = w; Model.Height = h;
        OnPropertyChanged(nameof(X)); OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(Width)); OnPropertyChanged(nameof(Height));
        _owner.ItemPreviewed(this);
    }

    public IReadOnlyList<ScreenIssue> Issues
    {
        get => _issues;
        set
        {
            _issues = value;
            OnPropertyChanged(nameof(Issues));
            OnPropertyChanged(nameof(IssueText));
            OnPropertyChanged(nameof(HasIssues));
            OnPropertyChanged(nameof(Severity));
        }
    }

    public string IssueText => string.Join("\n", _issues.Select(i => (i.Severity == IssueSeverity.Error ? "✖ " : "⚠ ") + i.Message));
    public bool HasIssues => _issues.Count > 0;

    /// <summary>"Error", "Warning" or "None"; drives colors in the list and on the canvas.</summary>
    public string Severity =>
        _issues.Any(i => i.Severity == IssueSeverity.Error) ? "Error" :
        _issues.Any(i => i.Severity == IssueSeverity.Warning) ? "Warning" : "None";
}

/// <summary>
/// Edits the screen layout. Screen definitions are never changed except by the operator: if the output
/// resolution changes or a screen is invalid, it is flagged, not corrected. Changes are saved at once.
/// </summary>
public sealed class ScreensViewModel : ObservableObject
{
    private readonly ScreenLayout _layout;
    private readonly Action _save;
    private readonly IAppLog _log;
    private readonly Func<CanvasInfo> _canvasProvider;
    private readonly Func<string, bool> _confirmRemove;
    private readonly MenusViewModel _menus;
    private CanvasInfo _canvas = new(null, CanvasSource.Unknown, null);
    private ScreenItemViewModel? _selected;
    private string _lastIssueSignature = "";
    private string? _saveError;

    private readonly Func<string, bool>? _mediaExists;
    private readonly VideoEditingServices? _video;
    private string? _videoMessage;

    public ScreensViewModel(ScreenLayout layout, Action save, IAppLog log,
        Func<CanvasInfo> canvasProvider, Func<string, bool> confirmRemove, MenusViewModel menus,
        Func<string, bool>? mediaExists = null, VideoEditingServices? video = null)
    {
        _menus = menus;
        _mediaExists = mediaExists;
        _video = video;
        _layout = layout;
        _save = save;
        _log = log;
        _canvasProvider = canvasProvider;
        _confirmRemove = confirmRemove;

        foreach (var s in _layout.Screens) Items.Add(new ScreenItemViewModel(s, this));
        _menus.ScreensUsing = id => _layout.Screens.Where(sc => sc.AssignedMenuId == id && !sc.ShowsVideo)
            .Select(sc => string.IsNullOrWhiteSpace(sc.Name) ? "(unnamed screen)" : sc.Name).ToList();
        _menus.MenusChanged += () => { foreach (var i in Items) i.RaiseMenusChanged(); Revalidate(); };
        AddCommand = new RelayCommand(() => Add(ScreenDefaults.Wall2x2Width, ScreenDefaults.Wall2x2Height));
        AddNarrowCommand = new RelayCommand(() => Add(ScreenDefaults.Wall1x2Width, ScreenDefaults.Wall1x2Height));
        DismissSaveErrorCommand = new RelayCommand(() => SaveError = null);
        CleanUpVideoFilesCommand = new RelayCommand(CleanUpVideoFiles, () => _video != null);
        DismissVideoMessageCommand = new RelayCommand(() => VideoMessage = null);
        Renumber();
        RefreshCanvas();
        _log.Info($"Screens loaded: {Items.Count}");
    }

    public ObservableCollection<ScreenItemViewModel> Items { get; } = new();

    internal IReadOnlyList<MenuChoice> MenuChoices => _menus.Choices;
    public RelayCommand AddCommand { get; }
    public RelayCommand AddNarrowCommand { get; }
    public RelayCommand DismissSaveErrorCommand { get; }
    public RelayCommand CleanUpVideoFilesCommand { get; }
    public RelayCommand DismissVideoMessageCommand { get; }

    /// <summary>True where videos can be imported at all.</summary>
    public bool CanManageVideo => _video != null;

    public string? VideoMessage
    {
        get => _videoMessage;
        private set { if (Set(ref _videoMessage, value)) OnPropertyChanged(nameof(HasVideoMessage)); }
    }
    public bool HasVideoMessage => !string.IsNullOrEmpty(_videoMessage);

    internal PlaylistEditorViewModel? CreatePlaylistEditor(ScreenItemViewModel item) =>
        _video == null ? null : new PlaylistEditorViewModel(item, _video);

    /// <summary>Every video file name any screen's playlist uses (whether or not that screen currently shows video).</summary>
    public IReadOnlySet<string> ReferencedMedia =>
        _layout.Screens.SelectMany(s => s.Playlist.Items).Select(i => i.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Deletes imported video files that no playlist uses any more, after asking. Playlists are never touched.</summary>
    public void CleanUpVideoFiles()
    {
        if (_video == null) return;
        if (!_video.Confirm("Delete the imported video files that no playlist uses any more?\n\nVideos that are in a playlist are not touched. The original files you imported from are not touched either.",
                "Remove unused video files")) return;
        var removed = _video.RemoveUnused(ReferencedMedia);
        _log.Info($"Unused video files removed: {removed.Count}");
        VideoMessage = removed.Count == 0 ? "There were no unused video files." : $"Removed {removed.Count} unused video file{(removed.Count == 1 ? "" : "s")}.";
        foreach (var i in Items) i.Playlist?.Refresh();
    }

    /// <summary>Raised whenever the canvas needs redrawing.</summary>
    public event Action? Changed;

    public CanvasInfo Canvas { get => _canvas; private set => Set(ref _canvas, value); }

    public ScreenItemViewModel? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            foreach (var i in Items) i.RaiseSelectionChanged();
            Changed?.Invoke();
        }
    }

    public string CanvasText => Canvas.Source switch
    {
        CanvasSource.Live => $"Output canvas: {Canvas.Size} pixels ({Canvas.DisplayName}, connected). Screens are checked against this size.",
        CanvasSource.LastKnown => $"Output display is not available. Checking against its last known size, {Canvas.Size} ({Canvas.DisplayName}).",
        _ => "No LED output display has been selected, so screens cannot be checked against an output size yet. The picture below uses a 1920×1080 stand-in.",
    };

    public string SummaryText
    {
        get
        {
            var errors = Items.Count(i => i.Severity == "Error");
            var warnings = Items.Count(i => i.Severity == "Warning");
            if (errors == 0 && warnings == 0) return "";
            var parts = new List<string>();
            if (errors > 0) parts.Add($"{errors} screen{(errors == 1 ? " has" : "s have")} errors and will not be drawn");
            if (warnings > 0) parts.Add($"{warnings} screen{(warnings == 1 ? " has" : "s have")} warnings");
            return string.Join("; ", parts) + ".";
        }
    }
    public bool HasSummary => SummaryText.Length > 0;
    public bool HasErrors => Items.Any(i => i.Severity == "Error");

    public string? SaveError
    {
        get => _saveError;
        private set { if (Set(ref _saveError, value)) OnPropertyChanged(nameof(HasSaveError)); }
    }
    public bool HasSaveError => !string.IsNullOrEmpty(_saveError);

    public bool HasNoScreens => Items.Count == 0;

    /// <summary>The size screens are validated against, or null if unknown.</summary>
    public CanvasSize? CanvasSizeOrNull => Canvas.Size;

    /// <summary>Width and height of the first enabled, valid screen that shows this menu; null if none does.</summary>
    public (int Width, int Height)? ScreenSizeForMenu(Guid menuId)
    {
        var plan = CalibrationPlan.Build(_layout.Screens, Canvas.Size);
        var s = plan.Drawn.FirstOrDefault(d => d.MenuId == menuId);
        return s == null ? null : (s.Width, s.Height);
    }

    /// <summary>Which screens a test pattern may be drawn on right now (valid and enabled), and which were skipped.</summary>
    /// <summary>The screen definitions themselves (the video playlists live on them).</summary>
    public IReadOnlyList<Screen> Models => _layout.Screens;

    public CalibrationPlan BuildCalibrationPlan(CanvasSize canvas) => CalibrationPlan.Build(_layout.Screens, canvas);

    /// <summary>Called when the output display or its size may have changed. Screen definitions are left alone.</summary>
    public void RefreshCanvas()
    {
        var info = _canvasProvider();
        if (info != Canvas) Canvas = info;
        OnPropertyChanged(nameof(CanvasText));
        Revalidate();
    }

    private void Add(int w, int h)
    {
        var (x, y) = ScreenPlacement.NextFreePosition(_layout.Screens, Canvas.Size, w, h);
        var screen = new Screen { Name = NextName(), X = x, Y = y, Width = w, Height = h };
        _layout.Screens.Add(screen);
        var item = new ScreenItemViewModel(screen, this);
        Items.Add(item);
        Renumber();
        Selected = item;
        _log.Info($"Screen added: {Describe(screen)}");
        CommitChange();
    }

    public void Remove(ScreenItemViewModel item)
    {
        var label = string.IsNullOrWhiteSpace(item.Name) ? $"Screen {item.Number}" : item.Name;
        if (!_confirmRemove(label)) return;

        _layout.Screens.Remove(item.Model);
        Items.Remove(item);
        if (ReferenceEquals(Selected, item)) Selected = null;
        Renumber();
        _log.Info($"Screen removed: {Describe(item.Model)}");
        CommitChange();
    }

    /// <summary>A property was edited in the list (committed text, checkbox).</summary>
    internal void ItemEdited(ScreenItemViewModel item)
    {
        _log.Info($"Screen edited: {Describe(item.Model)}");
        CommitChange();
    }

    /// <summary>The playlist of a video screen was edited (video added, moved, removed, switched, fit or loop changed).</summary>
    internal void PlaylistEdited(ScreenItemViewModel item, string what)
    {
        _log.Info($"Playlist edited on \"{item.Name}\": {what}");
        CommitChange();
    }

    /// <summary>Geometry changed mid-drag: update checks and picture, do not save yet.</summary>
    internal void ItemPreviewed(ScreenItemViewModel item)
    {
        Revalidate();
    }

    /// <summary>End of a drag: save once.</summary>
    public void CommitGeometry(ScreenItemViewModel item)
    {
        _log.Info($"Screen moved/resized: {Describe(item.Model)}");
        CommitChange();
    }

    private void CommitChange()
    {
        Revalidate();
        try
        {
            _save();
            SaveError = null;
        }
        catch (Exception ex)
        {
            _log.Error("Could not save screen layout.", ex);
            SaveError = "The screen layout could not be saved: " + ex.Message;
        }
    }

    private void Revalidate()
    {
        var issues = ScreenValidator.Validate(_layout.Screens, Canvas.Size, _menus.Ids, _mediaExists);
        foreach (var item in Items)
        {
            item.Issues = issues.Where(i => i.ScreenId == item.Model.Id).ToList();
            item.Playlist?.RefreshWarnings();
        }

        var signature = string.Join("|", issues.Select(i => $"{i.ScreenId}:{i.Code}:{i.Severity}"));
        if (signature != _lastIssueSignature)
        {
            _lastIssueSignature = signature;
            var errors = issues.Count(i => i.Severity == IssueSeverity.Error);
            var warnings = issues.Count - errors;
            if (issues.Count == 0) _log.Info("Screen validation: no problems.");
            else
            {
                _log.Warn($"Screen validation: {errors} error(s), {warnings} warning(s).");
                foreach (var i in issues)
                {
                    var name = Items.FirstOrDefault(x => x.Model.Id == i.ScreenId)?.Name;
                    _log.Warn($"  [{i.Severity}] \"{name}\": {i.Message}");
                }
            }
        }

        _menus.RefreshUsage();
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasSummary));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasNoScreens));
        Changed?.Invoke();
    }

    private void Renumber()
    {
        for (var i = 0; i < Items.Count; i++) Items[i].Number = i + 1;
        OnPropertyChanged(nameof(HasNoScreens));
    }

    private string NextName()
    {
        for (var n = Items.Count + 1; ; n++)
        {
            var name = $"Screen {n}";
            if (!_layout.Screens.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))) return name;
        }
    }

    private static string Describe(Screen s) =>
        $"\"{s.Name}\" at ({s.X},{s.Y}) {s.Width}×{s.Height} {(s.Enabled ? "enabled" : "disabled")}";
}
