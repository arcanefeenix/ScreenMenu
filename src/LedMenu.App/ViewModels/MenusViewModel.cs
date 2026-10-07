using System.Collections.ObjectModel;
using LedMenu.App.Infrastructure;
using System.Windows.Threading;
using LedMenu.App.Output;
using LedMenu.Core.Calibration;
using LedMenu.Core.Layout;
using LedMenu.Core.Logging;
using LedMenu.Core.Menus;
using LedMenu.Persistence;

namespace LedMenu.App.ViewModels;

/// <summary>One entry in a menu drop-down. <see cref="Guid.Empty"/> stands for "no menu".</summary>
public sealed record MenuChoice(Guid Id, string Name)
{
    /// <summary>What the drop-down shows, and what accessibility tools read.</summary>
    public override string ToString() => Name;
}

public sealed class MenuCardViewModel : ObservableObject
{
    private readonly MenusViewModel _owner;
    private IReadOnlyList<MenuIssue> _issues = Array.Empty<MenuIssue>();

    public MenuCardViewModel(Menu model, MenusViewModel owner)
    {
        Model = model;
        _owner = owner;
        SetLogoCommand = new RelayCommand(() => _owner.SetLogo(this));
        RemoveLogoCommand = new RelayCommand(() => _owner.RemoveLogo(this), () => !string.IsNullOrEmpty(Model.LogoAsset));
        DeleteCommand = new RelayCommand(() => _owner.Delete(this));
    }

    // ---- preview: the same pages the LED shows, drawn at whole-number zoom ----
    private bool _previewOpen;
    private PixelBuffer? _previewFrame;
    private int _zoom = 2;
    private bool _following = true;
    private int _pinnedPage;
    private string _previewStatus = "";
    private string _previewProblems = "";

    public bool IsPreviewOpen
    {
        get => _previewOpen;
        set { if (Set(ref _previewOpen, value)) _owner.RefreshPreview(this); }
    }
    public PixelBuffer? PreviewFrame { get => _previewFrame; set => Set(ref _previewFrame, value); }
    public int Zoom { get => _zoom; set { if (Set(ref _zoom, Math.Clamp(value, 1, 4))) { OnPropertyChanged(nameof(IsZoom1)); OnPropertyChanged(nameof(IsZoom2)); OnPropertyChanged(nameof(IsZoom3)); } } }
    public bool IsZoom1 => _zoom == 1;
    public bool IsZoom2 => _zoom == 2;
    public bool IsZoom3 => _zoom == 3;
    public bool Following { get => _following; set { if (Set(ref _following, value)) _owner.RefreshPreview(this); } }
    public int PinnedPage { get => _pinnedPage; set => Set(ref _pinnedPage, Math.Max(0, value)); }
    public string PreviewStatus { get => _previewStatus; set => Set(ref _previewStatus, value); }
    public string PreviewProblems { get => _previewProblems; set { if (Set(ref _previewProblems, value)) OnPropertyChanged(nameof(HasPreviewProblems)); } }
    public bool HasPreviewProblems => _previewProblems.Length > 0;
    public int LastPageCount { get; set; } = 1;
    public int ShownPage { get; set; }

    public RelayCommand PreviousPageCommand => new(() => _owner.StepPreview(this, -1));
    public RelayCommand NextPageCommand => new(() => _owner.StepPreview(this, +1));
    public RelayCommand FollowCommand => new(() => Following = true);
    public RelayCommand Zoom1Command => new(() => Zoom = 1);
    public RelayCommand Zoom2Command => new(() => Zoom = 2);
    public RelayCommand Zoom3Command => new(() => Zoom = 3);

    public Menu Model { get; }
    public RelayCommand SetLogoCommand { get; }
    public RelayCommand RemoveLogoCommand { get; }
    public RelayCommand DeleteCommand { get; }

    public string Name
    {
        get => Model.Name;
        set { if (Model.Name != value) { Model.Name = value ?? ""; OnPropertyChanged(); _owner.Edited(this); } }
    }

    public string HeaderText
    {
        get => Model.HeaderText;
        set { if (Model.HeaderText != value) { Model.HeaderText = value ?? ""; OnPropertyChanged(); _owner.Edited(this); } }
    }

    public string Subtitle
    {
        get => Model.Subtitle ?? "";
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? null : value;
            if (Model.Subtitle != v) { Model.Subtitle = v; OnPropertyChanged(); _owner.Edited(this); }
        }
    }

    /// <summary>How long each page is shown when the menu needs more than one (seconds, 2 to 120).</summary>
    public int PageSeconds
    {
        get => Model.Theme.PageSeconds;
        set
        {
            var v = Math.Clamp(value, (int)PageClock.MinSeconds, (int)PageClock.MaxSeconds);
            if (Model.Theme.PageSeconds == v) { OnPropertyChanged(); return; }
            Model.Theme.PageSeconds = v;
            OnPropertyChanged();
            _owner.Edited(this);
        }
    }

    public bool ShowPageIndicator
    {
        get => Model.Theme.ShowPageIndicator;
        set { if (Model.Theme.ShowPageIndicator != value) { Model.Theme.ShowPageIndicator = value; OnPropertyChanged(); _owner.Edited(this); } }
    }

    public string Summary
    {
        get
        {
            var items = Model.Items.Count;
            var hidden = Model.Items.Count(i => !i.Visible);
            var sold = Model.Items.Count(i => i.SoldOut);
            var text = $"{Model.Categories.Count} categor{(Model.Categories.Count == 1 ? "y" : "ies")}, {items} item{(items == 1 ? "" : "s")}";
            var extras = new List<string>();
            if (hidden > 0) extras.Add($"{hidden} hidden");
            if (sold > 0) extras.Add($"{sold} sold out");
            return extras.Count > 0 ? $"{text} ({string.Join(", ", extras)})" : text;
        }
    }

    public string LogoText => string.IsNullOrEmpty(Model.LogoAsset) ? "No logo" : "Logo: " + Model.LogoAsset;

    public string UsedBy { get; private set; } = "";

    public IReadOnlyList<MenuIssue> Issues
    {
        get => _issues;
        set
        {
            _issues = value;
            OnPropertyChanged(nameof(Issues));
            OnPropertyChanged(nameof(IssueText));
            OnPropertyChanged(nameof(HasIssues));
        }
    }
    public string IssueText => string.Join("\n", _issues.Select(i => "⚠ " + i.Message));
    public bool HasIssues => _issues.Count > 0;

    public void Refresh(string usedBy)
    {
        UsedBy = usedBy;
        OnPropertyChanged(nameof(UsedBy));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(LogoText));
        RemoveLogoCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>
/// Lists and manages menus. Every change is saved at once. Menus and screens stay independent: deleting a menu
/// leaves screens pointing at it, flagged, and the deleted file is kept in the trash folder.
/// </summary>
public sealed class MenusViewModel : ObservableObject
{
    private readonly MenuLibrary _library;
    private readonly AssetStore _assets;
    private readonly IAppLog _log;
    private readonly Func<string?> _pickImage;
    private readonly Func<string, string, bool> _confirm;
    private string? _message;

    private readonly MenuRenderService _render;
    private readonly PageClock _previewClock = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly DispatcherTimer _previewTimer;

    public MenusViewModel(MenuLibrary library, AssetStore assets, IAppLog log,
        Func<string?> pickImage, Func<string, string, bool> confirm, MenuRenderService render)
    {
        _render = render;
        _library = library;
        _assets = assets;
        _log = log;
        _pickImage = pickImage;
        _confirm = confirm;

        NewMenuCommand = new RelayCommand(NewMenu);
        AddSampleCommand = new RelayCommand<string>(AddSample);
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _previewTimer.Tick += (_, _) => { foreach (var c in Cards.Where(c => c.IsPreviewOpen)) RefreshPreview(c); };
        _previewTimer.Start();
        DismissMessageCommand = new RelayCommand(() => Message = null);
        Rebuild();
    }

    public ObservableCollection<MenuCardViewModel> Cards { get; } = new();

    /// <summary>For drop-downs: "(no menu)" first, then every loaded menu by name.</summary>
    public ObservableCollection<MenuChoice> Choices { get; } = new();

    public RelayCommand NewMenuCommand { get; }
    public RelayCommand<string> AddSampleCommand { get; }

    /// <summary>The ready-made sample menus offered for reviewing templates.</summary>
    public IReadOnlyList<SampleMenuInfo> Samples => SampleMenus.All;

    /// <summary>Size of the first enabled screen showing this menu, if any. The preview uses it so it matches the LED.</summary>
    public Func<Guid, (int Width, int Height)?> ScreenSizeFor { get; set; } = _ => null;

    /// <summary>Which page of this menu is on the LED output right now, if output is running it.</summary>
    public Func<Guid, (int Page, int Count)?> LivePageOf { get; set; } = _ => null;
    public RelayCommand DismissMessageCommand { get; }

    /// <summary>Names of the screens that point at a menu (supplied once screens exist).</summary>
    public Func<Guid, IReadOnlyList<string>> ScreensUsing { get; set; } = _ => Array.Empty<string>();

    /// <summary>Raised when menus were added, removed or renamed so screens can re-check their assignments.</summary>
    public event Action? MenusChanged;

    /// <summary>Raised when something inside a menu changed (an item, a price, Sold Out, Hide...). Does not change the list of menus.</summary>
    public event Action? ContentChanged;

    /// <summary>The editor saved a change to this menu: refresh its summary and tell the LED output and previews.</summary>
    public void NotifyContentChanged(Menu menu)
    {
        foreach (var c in Cards.Where(c => ReferenceEquals(c.Model, menu))) { c.Refresh(c.UsedBy); c.Issues = MenuValidator.Issues(c.Model, _assets.Exists); }
        ContentChanged?.Invoke();
    }

    public IReadOnlySet<Guid> Ids => _library.Ids;
    public bool HasNoMenus => Cards.Count == 0;

    public IReadOnlyList<MenuLoadIssue> LoadIssues => _library.LoadIssues;
    public string? LoadNotice => LoadIssues.Count == 0 ? null : string.Join("\n", LoadIssues.Select(i => i.Message));
    public bool HasLoadNotice => LoadIssues.Count > 0;

    public string? Message
    {
        get => _message;
        private set { if (Set(ref _message, value)) OnPropertyChanged(nameof(HasMessage)); }
    }
    public bool HasMessage => !string.IsNullOrEmpty(_message);

    /// <summary>The menu with this id, or null.</summary>
    public Menu? Find(Guid id) => _library.Find(id);

    // ---- commands ------------------------------------------------------------------------

    private void NewMenu()
    {
        var n = 1;
        string name;
        do name = $"Menu {n++}"; while (_library.Menus.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)));
        Guard(() => _library.Create(name), "create the menu");
        Rebuild();
    }

    private void AddSample(string? key)
    {
        var info = SampleMenus.All.FirstOrDefault(x => x.Key == key);
        if (info == null) return;
        var sample = info.Create();
        var baseName = sample.Name;
        var n = 2;
        while (_library.Menus.Any(m => string.Equals(m.Name, sample.Name, StringComparison.OrdinalIgnoreCase)))
            sample.Name = $"{baseName} {n++}";
        Guard(() => _library.Add(sample), "add the sample menu");
        Rebuild();
    }

    internal void Edited(MenuCardViewModel card)
    {
        Guard(() => _library.Save(card.Model), "save the menu");
        card.Refresh(card.UsedBy);
        card.Issues = MenuValidator.Issues(card.Model, _assets.Exists);
        RebuildChoices();
        MenusChanged?.Invoke();
    }

    internal void SetLogo(MenuCardViewModel card)
    {
        var path = _pickImage();
        if (path == null) return;
        try
        {
            var result = _assets.Import(path);
            card.Model.LogoAsset = result.FileName;
            Guard(() => _library.Save(card.Model), "save the menu");
            _log.Info($"Logo set for \"{card.Model.Name}\": {result.FileName}");
            Message = null;
        }
        catch (AssetImportException ex)
        {
            _log.Warn($"Logo import failed: {ex.Message}");
            Message = "The logo could not be added: " + ex.Message;
        }
        card.Refresh(card.UsedBy);
        card.Issues = MenuValidator.Issues(card.Model, _assets.Exists);
    }

    internal void RemoveLogo(MenuCardViewModel card)
    {
        card.Model.LogoAsset = null;
        Guard(() => _library.Save(card.Model), "save the menu");
        card.Refresh(card.UsedBy);
        card.Issues = MenuValidator.Issues(card.Model, _assets.Exists);
    }

    internal void Delete(MenuCardViewModel card)
    {
        var users = ScreensUsing(card.Model.Id);
        var warning = users.Count == 0
            ? "No screen uses this menu."
            : "These screens use this menu and will be flagged until you pick another: " + string.Join(", ", users) + ".";
        var label = string.IsNullOrWhiteSpace(card.Name) ? "this menu" : $"\"{card.Name}\"";
        if (!_confirm($"Delete {label}?\n\n{warning}\n\nA copy is kept in the trash folder.", "Delete menu")) return;

        Guard(() => _library.Delete(card.Model.Id), "delete the menu");
        Rebuild();
    }

    // ---- preview ---------------------------------------------------------------------------

    internal void RefreshPreview(MenuCardViewModel card)
    {
        if (!card.IsPreviewOpen) { card.PreviewFrame = null; return; }

        var (w, h) = ScreenSizeFor(card.Model.Id) ?? (336, 672);
        var result = _render.Get(card.Model, w, h);
        card.LastPageCount = result.PageCount;

        int page;
        string mode;
        if (card.Following)
        {
            if (LivePageOf(card.Model.Id) is { } live) { page = live.Page; mode = "following the LED output"; }
            else
            {
                page = _previewClock.PageFor(card.Model.Id, result.PageCount, PageClock.Period(card.Model.Theme.PageSeconds), _clock.Elapsed);
                mode = "rotating like the LED output will";
            }
        }
        else
        {
            page = Math.Clamp(card.PinnedPage, 0, result.PageCount - 1);
            mode = "held on this page (the LED output is not affected)";
        }
        page = Math.Clamp(page, 0, result.PageCount - 1);
        card.ShownPage = page;
        card.PreviewFrame = result.Pages[page];
        card.PreviewStatus = $"{w}\u00D7{h}   Page {page + 1} of {result.PageCount}   ({mode})";
        card.PreviewProblems = string.Join("\n", result.Problems.Select(p => "\u26A0 " + p.Message));
    }

    internal void StepPreview(MenuCardViewModel card, int delta)
    {
        // browsing pins the preview to a page; the LED output keeps rotating on its own
        var count = Math.Max(1, card.LastPageCount);
        card.PinnedPage = ((card.ShownPage + delta) % count + count) % count;
        card.Following = false;
        RefreshPreview(card);
    }

    // ---- rebuild -------------------------------------------------------------------------

    /// <summary>Refreshes the cards' "used by" text (called when screens change).</summary>
    public void RefreshUsage()
    {
        foreach (var c in Cards)
        {
            var users = ScreensUsing(c.Model.Id);
            c.Refresh(users.Count == 0 ? "Not used by any screen" : "Used by: " + string.Join(", ", users));
        }
    }

    private void Rebuild()
    {
        Cards.Clear();
        foreach (var m in _library.Menus)
        {
            var card = new MenuCardViewModel(m, this) { };
            card.Issues = MenuValidator.Issues(m, _assets.Exists);
            Cards.Add(card);
        }
        RebuildChoices();
        RefreshUsage();
        OnPropertyChanged(nameof(HasNoMenus));
        MenusChanged?.Invoke();
    }

    private void RebuildChoices()
    {
        Choices.Clear();
        Choices.Add(new MenuChoice(Guid.Empty, "(no menu)"));
        foreach (var m in _library.Menus)
            Choices.Add(new MenuChoice(m.Id, string.IsNullOrWhiteSpace(m.Name) ? "(unnamed menu)" : m.Name));
    }

    private void Guard(Action action, string what)
    {
        try { action(); Message = null; }
        catch (Exception ex)
        {
            _log.Error($"Could not {what}.", ex);
            Message = $"Could not {what}: {ex.Message}";
        }
    }
}
