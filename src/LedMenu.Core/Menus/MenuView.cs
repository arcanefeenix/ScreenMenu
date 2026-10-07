namespace LedMenu.Core.Menus;

public sealed record MenuViewGroup(MenuCategory? Category, IReadOnlyList<MenuItem> Items);

/// <summary>
/// What a renderer should draw for a menu: only visible things, in order, grouped by category.
/// Hidden items and items of hidden categories are gone, and a category with nothing left to show
/// produces no header. Sold-out items remain (the template decides how to mark them).
/// </summary>
public sealed record MenuView(string Header, string? Subtitle, string? LogoAsset, IReadOnlyList<MenuViewGroup> Groups)
{
    public int ItemCount => Groups.Sum(g => g.Items.Count);
    public bool IsEmpty => ItemCount == 0;

    public static MenuView Build(Menu menu)
    {
        var groups = new List<MenuViewGroup>();

        foreach (var category in MenuOrdering.Categories(menu))
        {
            if (!category.Visible) continue;
            var items = MenuOrdering.Items(menu, category.Id).Where(i => i.Visible).ToList();
            if (items.Count > 0) groups.Add(new MenuViewGroup(category, items));
        }

        var loose = MenuOrdering.Items(menu, null).Where(i => i.Visible).ToList();
        if (loose.Count > 0) groups.Add(new MenuViewGroup(null, loose));

        var header = string.IsNullOrWhiteSpace(menu.HeaderText) ? menu.Name : menu.HeaderText;
        var subtitle = string.IsNullOrWhiteSpace(menu.Subtitle) ? null : menu.Subtitle;
        return new MenuView(header, subtitle, string.IsNullOrWhiteSpace(menu.LogoAsset) ? null : menu.LogoAsset, groups);
    }
}
