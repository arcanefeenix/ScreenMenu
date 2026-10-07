using LedMenu.Core.Logging;

namespace LedMenu.Core.Menus;

/// <summary>One thing that happened while loading menu files that the operator should be told about.</summary>
public sealed record MenuLoadIssue(string File, string Message, bool Recovered);

public sealed record MenuLoadResult(IReadOnlyList<Menu> Menus, IReadOnlyList<MenuLoadIssue> Issues);

/// <summary>Where menus are kept. The real implementation is one JSON file per menu; tests use memory.</summary>
public interface IMenuStore
{
    MenuLoadResult LoadAll();
    void Save(Menu menu);
    /// <summary>Takes the menu out of use. Implementations keep a recoverable copy rather than destroying it.</summary>
    void Delete(Menu menu);
}

/// <summary>
/// The set of menus. Every change is saved straight away through the store. Menus are independent of
/// screens: deleting a menu never edits a screen, it only makes any assignment to it visibly dangling.
/// </summary>
public sealed class MenuLibrary
{
    private readonly IMenuStore _store;
    private readonly IAppLog _log;
    private readonly List<Menu> _menus = new();

    public MenuLibrary(IMenuStore store, IAppLog? log = null)
    {
        _store = store;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>Alphabetical by name (case-insensitive), then by id, so the list order is stable.</summary>
    public IReadOnlyList<Menu> Menus =>
        _menus.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Id).ToList();

    public IReadOnlyList<MenuLoadIssue> LoadIssues { get; private set; } = Array.Empty<MenuLoadIssue>();

    public IReadOnlySet<Guid> Ids => _menus.Select(m => m.Id).ToHashSet();

    public Menu? Find(Guid id) => _menus.FirstOrDefault(m => m.Id == id);

    public void Load()
    {
        var result = _store.LoadAll();
        _menus.Clear();

        var issues = result.Issues.ToList();
        foreach (var m in result.Menus)
        {
            if (_menus.Any(x => x.Id == m.Id))
            {
                issues.Add(new MenuLoadIssue(m.Name, $"A second menu with the same id as \"{m.Name}\" was found and was not loaded.", false));
                continue;
            }
            _menus.Add(m);
        }
        LoadIssues = issues;
        _log.Info($"Menus loaded: {_menus.Count} menu(s), {issues.Count} issue(s).");
        foreach (var i in issues) _log.Warn($"Menu load: {i.File}: {i.Message}");
    }

    public Menu Create(string name)
    {
        var menu = new Menu { Name = name, HeaderText = name };
        Add(menu);
        return menu;
    }

    /// <summary>Adds a ready-made menu (for example a sample) and saves it.</summary>
    public void Add(Menu menu)
    {
        if (_menus.Any(m => m.Id == menu.Id)) throw new InvalidOperationException("A menu with this id already exists.");
        _store.Save(menu);
        _menus.Add(menu);
        _log.Info($"Menu created: \"{menu.Name}\" ({menu.Id}).");
    }

    /// <summary>Saves a menu after it was edited.</summary>
    public void Save(Menu menu)
    {
        if (!_menus.Contains(menu)) throw new InvalidOperationException("That menu is not part of this library.");
        _store.Save(menu);
    }

    public void Delete(Guid id)
    {
        var menu = Find(id) ?? throw new ArgumentException($"No menu with id {id}.", nameof(id));
        _store.Delete(menu);
        _menus.Remove(menu);
        _log.Info($"Menu deleted (a recoverable copy is kept): \"{menu.Name}\" ({menu.Id}).");
    }
}
