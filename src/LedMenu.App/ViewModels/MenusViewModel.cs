using System.Collections.ObjectModel;
using LedMenu.App.Infrastructure;
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

    public MenusViewModel(MenuLibrary library, AssetStore assets, IAppLog log,
        Func<string?> pickImage, Func<string, string, bool> confirm)
    {
        _library = library;
        _assets = assets;
        _log = log;
        _pickImage = pickImage;
        _confirm = confirm;

        NewMenuCommand = new RelayCommand(NewMenu);
        NewSampleCommand = new RelayCommand(NewSample);
        DismissMessageCommand = new RelayCommand(() => Message = null);
        Rebuild();
    }

    public ObservableCollection<MenuCardViewModel> Cards { get; } = new();

    /// <summary>For drop-downs: "(no menu)" first, then every loaded menu by name.</summary>
    public ObservableCollection<MenuChoice> Choices { get; } = new();

    public RelayCommand NewMenuCommand { get; }
    public RelayCommand NewSampleCommand { get; }
    public RelayCommand DismissMessageCommand { get; }

    /// <summary>Names of the screens that point at a menu (supplied once screens exist).</summary>
    public Func<Guid, IReadOnlyList<string>> ScreensUsing { get; set; } = _ => Array.Empty<string>();

    /// <summary>Raised when menus were added, removed or renamed so screens can re-check their assignments.</summary>
    public event Action? MenusChanged;

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

    private void NewSample()
    {
        var sample = SampleMenus.FestivalFood();
        if (_library.Menus.Any(m => string.Equals(m.Name, sample.Name, StringComparison.OrdinalIgnoreCase)))
            sample.Name = sample.Name + " (sample)";
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
