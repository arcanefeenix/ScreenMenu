using System.Collections.ObjectModel;
using LedMenu.App.Infrastructure;
using LedMenu.Core.Autosave;
using LedMenu.Core.Logging;
using LedMenu.Core.Menus;

namespace LedMenu.App.ViewModels;

public enum DeleteCategoryChoice { Cancel, DeleteItemsToo, KeepItems }

/// <summary>One item row in the editor: the everyday controls (price, Sold Out, Hide) plus an Edit section.</summary>
public sealed class EditorItemViewModel : ObservableObject
{
    private readonly MenuEditorViewModel _owner;
    private bool _isEditing;

    public EditorItemViewModel(MenuItem model, MenuEditorViewModel owner, bool isEditing)
    {
        Model = model;
        _owner = owner;
        _isEditing = isEditing;
        ToggleSoldOutCommand = new RelayCommand(() => _owner.ToggleSoldOut(this));
        ToggleVisibleCommand = new RelayCommand(() => _owner.ToggleVisible(this));
        MoveUpCommand = new RelayCommand(() => _owner.MoveItem(this, -1));
        MoveDownCommand = new RelayCommand(() => _owner.MoveItem(this, +1));
        DeleteCommand = new RelayCommand(() => _owner.DeleteItem(this));
    }

    public MenuItem Model { get; }
    public RelayCommand ToggleSoldOutCommand { get; }
    public RelayCommand ToggleVisibleCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand DeleteCommand { get; }

    // typed text: applied to the menu at once, saved and shown on the LED after a short pause in typing
    public string Name { get => Model.Name; set { if (Model.Name != value) { Model.Name = value ?? ""; OnPropertyChanged(); _owner.TextEdited(this); } } }
    public string Price { get => Model.Price; set { if (Model.Price != value) { Model.Price = value ?? ""; OnPropertyChanged(); _owner.TextEdited(this); } } }
    public string Description { get => Model.Description; set { if (Model.Description != value) { Model.Description = value ?? ""; OnPropertyChanged(); _owner.TextEdited(this); } } }

    public bool Featured
    {
        get => Model.Featured;
        set { if (Model.Featured != value) { Model.Featured = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); _owner.Discrete(this); } }
    }

    public bool IsEditing { get => _isEditing; set { if (Set(ref _isEditing, value)) _owner.ItemEditingChanged(this); } }

    public bool SoldOut => Model.SoldOut;
    public bool IsVisible => Model.Visible;
    public string SoldOutButtonText => Model.SoldOut ? "Back in Stock" : "Sold Out";
    public string VisibilityButtonText => Model.Visible ? "Hide" : "Show";
    public double RowOpacity => Model.Visible ? 1.0 : 0.45;

    public string StatusText
    {
        get
        {
            var parts = new List<string>();
            if (!Model.Visible) parts.Add("HIDDEN");
            if (Model.SoldOut) parts.Add("SOLD OUT");
            if (Model.Featured) parts.Add("FEATURED");
            return string.Join("  •  ", parts);
        }
    }

    public IReadOnlyList<MenuChoice> CategoryChoices => _owner.CategoryChoices;

    /// <summary>The item's category, or <see cref="Guid.Empty"/> for none. Changing it moves the item to the end of that category.</summary>
    public Guid CategoryKey
    {
        get => MenuOrdering.EffectiveCategoryId(_owner.SelectedMenu!, Model) ?? Guid.Empty;
        set => _owner.MoveItemToCategory(this, value);
    }

    public void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(SoldOut));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(SoldOutButtonText));
        OnPropertyChanged(nameof(VisibilityButtonText));
        OnPropertyChanged(nameof(RowOpacity));
        OnPropertyChanged(nameof(StatusText));
    }
}

/// <summary>A category section (or the "no category" section when <see cref="Model"/> is null).</summary>
public sealed class EditorCategoryViewModel : ObservableObject
{
    private readonly MenuEditorViewModel _owner;

    public EditorCategoryViewModel(MenuCategory? model, MenuEditorViewModel owner)
    {
        Model = model;
        _owner = owner;
        AddItemCommand = new RelayCommand(() => _owner.AddItem(this));
        ToggleVisibleCommand = new RelayCommand(() => _owner.ToggleCategoryVisible(this), () => Model != null);
        MoveUpCommand = new RelayCommand(() => _owner.MoveCategory(this, -1), () => Model != null);
        MoveDownCommand = new RelayCommand(() => _owner.MoveCategory(this, +1), () => Model != null);
        DeleteCommand = new RelayCommand(() => _owner.DeleteCategory(this), () => Model != null);
    }

    public MenuCategory? Model { get; }
    public bool IsUncategorized => Model == null;
    public ObservableCollection<EditorItemViewModel> Items { get; } = new();

    public RelayCommand AddItemCommand { get; }
    public RelayCommand ToggleVisibleCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand DeleteCommand { get; }

    public string Name
    {
        get => Model?.Name ?? "No category";
        set
        {
            if (Model == null || Model.Name == value) return;
            Model.Name = value ?? "";
            OnPropertyChanged();
            _owner.TextEdited(this);
        }
    }

    public bool IsVisible => Model?.Visible ?? true;
    public string VisibilityButtonText => IsVisible ? "Hide Category" : "Show Category";
    public double RowOpacity => IsVisible ? 1.0 : 0.5;

    public void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(VisibilityButtonText));
        OnPropertyChanged(nameof(RowOpacity));
    }
}

/// <summary>
/// The everyday menu editor: pick a menu, then price, Sold Out, Hide/Show, add, edit, delete and reorder items and
/// categories. There is no publish step. Quick actions are saved and shown on the LED at once; typed text is applied to
/// the menu immediately and saved/shown after a short pause in typing (see <see cref="SaveDebouncer"/>).
/// </summary>
public sealed class MenuEditorViewModel : ObservableObject
{
    private readonly MenuLibrary _library;
    private readonly SaveDebouncer _saver;
    private readonly IAppLog _log;
    private readonly Func<string, string, bool> _confirm;
    private readonly Func<string, string, DeleteCategoryChoice> _askDeleteCategory;
    private readonly Action<Menu> _contentChanged;
    private readonly Func<Menu, (string? Problems, string Info)> _inspect;
    private readonly HashSet<Guid> _editingItems = new();
    private Guid _selectedMenuId = Guid.Empty;
    private string? _message;
    private string? _problems;
    private string _info = "";

    public MenuEditorViewModel(MenuLibrary library, SaveDebouncer saver, IAppLog log,
        Func<string, string, bool> confirm, Func<string, string, DeleteCategoryChoice> askDeleteCategory,
        Action<Menu> contentChanged, Func<Menu, (string? Problems, string Info)> inspect)
    {
        _library = library;
        _saver = saver;
        _log = log;
        _confirm = confirm;
        _askDeleteCategory = askDeleteCategory;
        _contentChanged = contentChanged;
        _inspect = inspect;

        AddCategoryCommand = new RelayCommand(AddCategory, () => SelectedMenu != null);
        AddLooseItemCommand = new RelayCommand(() => AddItemTo(null), () => SelectedMenu != null);
        DismissMessageCommand = new RelayCommand(() => Message = null);
        RefreshMenus();
    }

    public ObservableCollection<MenuChoice> MenuChoices { get; } = new();
    public ObservableCollection<EditorCategoryViewModel> Groups { get; } = new();
    public ObservableCollection<MenuChoice> CategoryChoicesList { get; } = new();
    public IReadOnlyList<MenuChoice> CategoryChoices => CategoryChoicesList;

    public RelayCommand AddCategoryCommand { get; }
    public RelayCommand AddLooseItemCommand { get; }
    public RelayCommand DismissMessageCommand { get; }

    public Menu? SelectedMenu => _library.Find(_selectedMenuId);
    public bool HasMenu => SelectedMenu != null;
    public bool HasNoMenu => SelectedMenu == null;

    /// <summary>The menu being edited, as an id for the drop-down. Choosing another menu saves anything still waiting first.</summary>
    public Guid SelectedMenuId
    {
        get => _selectedMenuId;
        set
        {
            if (value == _selectedMenuId) return;
            _saver.FlushAll();
            _selectedMenuId = value;
            OnPropertyChanged();
            Rebuild();
            OnPropertyChanged(nameof(HasMenu));
            OnPropertyChanged(nameof(HasNoMenu));
        }
    }

    public string? Message { get => _message; private set { if (Set(ref _message, value)) OnPropertyChanged(nameof(HasMessage)); } }
    public bool HasMessage => !string.IsNullOrEmpty(_message);
    public string? Problems { get => _problems; private set { if (Set(ref _problems, value)) OnPropertyChanged(nameof(HasProblems)); } }
    public bool HasProblems => !string.IsNullOrEmpty(_problems);
    public string Info { get => _info; private set => Set(ref _info, value); }

    /// <summary>Saves everything still waiting. Call when focus leaves a text box and when the program exits.</summary>
    public void FlushPending() => _saver.FlushAll();

    /// <summary>The set of menus changed (created, renamed, deleted): refresh the drop-down, keep the current menu if it still exists.</summary>
    public void RefreshMenus()
    {
        MenuChoices.Clear();
        foreach (var m in _library.Menus)
            MenuChoices.Add(new MenuChoice(m.Id, string.IsNullOrWhiteSpace(m.Name) ? "(unnamed menu)" : m.Name));

        if (_library.Find(_selectedMenuId) == null)
        {
            _selectedMenuId = _library.Menus.FirstOrDefault()?.Id ?? Guid.Empty;
            OnPropertyChanged(nameof(SelectedMenuId));
            Rebuild();
        }
        else OnPropertyChanged(nameof(SelectedMenuId));

        OnPropertyChanged(nameof(HasMenu));
        OnPropertyChanged(nameof(HasNoMenu));
        AddCategoryCommand.RaiseCanExecuteChanged();
        AddLooseItemCommand.RaiseCanExecuteChanged();
    }

    // ---- quick actions (saved and shown at once) ---------------------------------------------

    internal void ToggleSoldOut(EditorItemViewModel item)
    {
        var menu = Current();
        MenuEditor.SetSoldOut(menu, item.Model.Id, !item.Model.SoldOut);
        _log.Info($"\"{menu.Name}\": \"{item.Model.Name}\" {(item.Model.SoldOut ? "marked sold out" : "back in stock")}.");
        item.RaiseStateChanged();
        Discrete(item);
    }

    internal void ToggleVisible(EditorItemViewModel item)
    {
        var menu = Current();
        MenuEditor.SetVisible(menu, item.Model.Id, !item.Model.Visible);
        _log.Info($"\"{menu.Name}\": \"{item.Model.Name}\" {(item.Model.Visible ? "shown" : "hidden")}.");
        item.RaiseStateChanged();
        Discrete(item);
    }

    internal void ToggleCategoryVisible(EditorCategoryViewModel group)
    {
        if (group.Model == null) return;
        MenuEditor.SetCategoryVisible(Current(), group.Model.Id, !group.Model.Visible);
        group.RaiseStateChanged();
        Discrete(null);
    }

    // ---- structure (saved and shown at once, list redrawn) --------------------------------------

    internal void AddItem(EditorCategoryViewModel group) => AddItemTo(group.Model?.Id);

    private void AddItemTo(Guid? categoryId)
    {
        var menu = Current();
        var item = MenuEditor.AddItem(menu, categoryId, "New item");
        _editingItems.Add(item.Id);                // open for typing straight away
        Discrete(null);
        Rebuild();
    }

    private void AddCategory()
    {
        var menu = Current();
        MenuEditor.AddCategory(menu, "New category");
        Discrete(null);
        Rebuild();
    }

    internal void DeleteItem(EditorItemViewModel item)
    {
        var label = string.IsNullOrWhiteSpace(item.Model.Name) ? "this item" : $"\"{item.Model.Name}\"";
        if (!_confirm($"Delete {label}?\n\nTo take it off the display for now, use Hide instead.", "Delete item")) return;
        MenuEditor.RemoveItem(Current(), item.Model.Id);
        _editingItems.Remove(item.Model.Id);
        Discrete(null);
        Rebuild();
    }

    internal void DeleteCategory(EditorCategoryViewModel group)
    {
        if (group.Model == null) return;
        var menu = Current();
        var count = MenuOrdering.Items(menu, group.Model.Id).Count;
        var label = string.IsNullOrWhiteSpace(group.Model.Name) ? "this category" : $"\"{group.Model.Name}\"";

        bool deleteItems;
        if (count == 0)
        {
            if (!_confirm($"Delete {label}?", "Delete category")) return;
            deleteItems = false;
        }
        else
        {
            var choice = _askDeleteCategory($"{label} has {count} item{(count == 1 ? "" : "s")}.", "Delete category");
            if (choice == DeleteCategoryChoice.Cancel) return;
            deleteItems = choice == DeleteCategoryChoice.DeleteItemsToo;
        }
        MenuEditor.RemoveCategory(menu, group.Model.Id, deleteItems);
        Discrete(null);
        Rebuild();
    }

    internal void MoveItem(EditorItemViewModel item, int delta)
    {
        var menu = Current();
        var siblings = MenuOrdering.Items(menu, MenuOrdering.EffectiveCategoryId(menu, item.Model));
        var index = siblings.ToList().FindIndex(i => i.Id == item.Model.Id);
        if (index < 0) return;
        MenuEditor.MoveItem(menu, item.Model.Id, index + delta);
        Discrete(null);
        Rebuild();
    }

    internal void MoveCategory(EditorCategoryViewModel group, int delta)
    {
        if (group.Model == null) return;
        var menu = Current();
        var index = MenuOrdering.Categories(menu).ToList().FindIndex(c => c.Id == group.Model.Id);
        MenuEditor.MoveCategory(menu, group.Model.Id, index + delta);
        Discrete(null);
        Rebuild();
    }

    internal void MoveItemToCategory(EditorItemViewModel item, Guid categoryKey)
    {
        var menu = Current();
        Guid? target = categoryKey == Guid.Empty ? null : categoryKey;
        if (MenuOrdering.EffectiveCategoryId(menu, item.Model) == target) return;
        MenuEditor.MoveItemToCategory(menu, item.Model.Id, target, int.MaxValue);
        Discrete(null);
        Rebuild();
    }

    internal void ItemEditingChanged(EditorItemViewModel item)
    {
        if (item.IsEditing) _editingItems.Add(item.Model.Id); else _editingItems.Remove(item.Model.Id);
    }

    // ---- saving ------------------------------------------------------------------------------

    /// <summary>Typed text: already in the menu; save and announce after a pause in typing.</summary>
    internal void TextEdited(object _)
    {
        var menu = SelectedMenu;
        if (menu == null) return;
        _saver.Request(menu.Id, () => SaveAndAnnounce(menu));
    }

    /// <summary>A button press or checkbox: save and announce now.</summary>
    internal void Discrete(object? _)
    {
        var menu = SelectedMenu;
        if (menu == null) return;
        _saver.Immediate(menu.Id, () => SaveAndAnnounce(menu));
    }

    /// <summary>How long to wait before trying again after a save failed (disk full, file locked by another program).</summary>
    public static readonly TimeSpan SaveRetryDelay = TimeSpan.FromSeconds(5);

    private void SaveAndAnnounce(Menu menu)
    {
        try
        {
            _library.Save(menu);
            Message = null;
        }
        catch (Exception ex)
        {
            _log.Error($"Could not save menu \"{menu.Name}\".", ex);
            Message = $"The menu could not be saved: {ex.Message}. Your changes are still on screen; saving will be retried every few seconds and when you close the program.";
            // keep trying: the edit is still in memory, and closing the program flushes this pending retry once more
            _saver.Request(menu.Id, () => SaveAndAnnounce(menu), SaveRetryDelay);
        }
        _contentChanged(menu);
        Inspect(menu);
    }

    private void Inspect(Menu menu)
    {
        var (problems, info) = _inspect(menu);
        Problems = problems;
        Info = info;
    }

    // ---- building the lists ------------------------------------------------------------------

    private Menu Current() => SelectedMenu ?? throw new InvalidOperationException("No menu is selected.");

    private void Rebuild()
    {
        Groups.Clear();
        CategoryChoicesList.Clear();
        var menu = SelectedMenu;
        if (menu == null) { Problems = null; Info = ""; return; }

        CategoryChoicesList.Add(new MenuChoice(Guid.Empty, "(no category)"));
        foreach (var c in MenuOrdering.Categories(menu))
            CategoryChoicesList.Add(new MenuChoice(c.Id, string.IsNullOrWhiteSpace(c.Name) ? "(unnamed category)" : c.Name));

        foreach (var c in MenuOrdering.Categories(menu))
        {
            var g = new EditorCategoryViewModel(c, this);
            foreach (var i in MenuOrdering.Items(menu, c.Id)) g.Items.Add(new EditorItemViewModel(i, this, _editingItems.Contains(i.Id)));
            Groups.Add(g);
        }
        var loose = MenuOrdering.Items(menu, null);
        if (loose.Count > 0)
        {
            var g = new EditorCategoryViewModel(null, this);
            foreach (var i in loose) g.Items.Add(new EditorItemViewModel(i, this, _editingItems.Contains(i.Id)));
            Groups.Add(g);
        }
        Inspect(menu);
        AddCategoryCommand.RaiseCanExecuteChanged();
        AddLooseItemCommand.RaiseCanExecuteChanged();
    }
}
