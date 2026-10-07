namespace LedMenu.Core.Menus;

/// <summary>
/// The one definition of order. Categories and items sort by SortOrder; ties keep their position in the
/// stored list, so the result is always the same for the same data. Hidden items and categories are still
/// part of the order (so unhiding puts them back where they were).
/// </summary>
public static class MenuOrdering
{
    public static IReadOnlyList<MenuCategory> Categories(Menu menu) =>
        menu.Categories
            .Select((c, i) => (c, i))
            .OrderBy(t => t.c.SortOrder).ThenBy(t => t.i)
            .Select(t => t.c)
            .ToList();

    /// <summary>The category an item really belongs to: its CategoryId if that category exists, otherwise null (uncategorized).</summary>
    public static Guid? EffectiveCategoryId(Menu menu, MenuItem item) =>
        item.CategoryId is { } id && menu.Categories.Any(c => c.Id == id) ? id : null;

    /// <summary>Items of one category in order. Pass null for uncategorized (including items pointing at a missing category).</summary>
    public static IReadOnlyList<MenuItem> Items(Menu menu, Guid? categoryId) =>
        menu.Items
            .Select((it, i) => (it, i))
            .Where(t => EffectiveCategoryId(menu, t.it) == categoryId)
            .OrderBy(t => t.it.SortOrder).ThenBy(t => t.i)
            .Select(t => t.it)
            .ToList();

    /// <summary>Rewrites SortOrder as 0, 1, 2... in the current effective order, so later edits start from a clean sequence.</summary>
    public static void Normalize(Menu menu)
    {
        var order = 0;
        foreach (var c in Categories(menu)) c.SortOrder = order++;

        foreach (var group in new Guid?[] { null }.Concat(menu.Categories.Select(c => (Guid?)c.Id)))
        {
            var n = 0;
            foreach (var it in Items(menu, group)) it.SortOrder = n++;
        }
    }
}
