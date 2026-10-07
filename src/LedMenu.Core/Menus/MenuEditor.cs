namespace LedMenu.Core.Menus;

/// <summary>
/// Operations on a menu. They keep SortOrder consistent and never leave dangling references behind.
/// An unknown id throws <see cref="ArgumentException"/> so a bug cannot silently edit nothing.
/// </summary>
public static class MenuEditor
{
    // ---- categories ----

    public static MenuCategory AddCategory(Menu menu, string name)
    {
        var next = menu.Categories.Count == 0 ? 0 : menu.Categories.Max(c => c.SortOrder) + 1;
        var category = new MenuCategory { Name = name, SortOrder = next };
        menu.Categories.Add(category);
        return category;
    }

    /// <summary>
    /// Removes a category. Its items are deleted too when <paramref name="deleteItems"/> is true;
    /// otherwise they stay and become uncategorized.
    /// </summary>
    public static void RemoveCategory(Menu menu, Guid categoryId, bool deleteItems)
    {
        var category = Category(menu, categoryId);
        var members = MenuOrdering.Items(menu, categoryId).ToList();
        if (deleteItems)
            foreach (var it in members) menu.Items.Remove(it);
        else
        {
            var tail = MenuOrdering.Items(menu, null).Count;
            foreach (var it in members) { it.CategoryId = null; it.SortOrder = tail++; }
        }
        menu.Categories.Remove(category);
        MenuOrdering.Normalize(menu);
    }

    /// <summary>Moves a category to a zero-based position in the ordered category list (clamped to the ends).</summary>
    public static void MoveCategory(Menu menu, Guid categoryId, int newIndex)
    {
        var category = Category(menu, categoryId);
        var list = MenuOrdering.Categories(menu).ToList();
        list.Remove(category);
        list.Insert(Math.Clamp(newIndex, 0, list.Count), category);
        for (var i = 0; i < list.Count; i++) list[i].SortOrder = i;
    }

    public static void SetCategoryVisible(Menu menu, Guid categoryId, bool visible) =>
        Category(menu, categoryId).Visible = visible;

    // ---- items ----

    public static MenuItem AddItem(Menu menu, Guid? categoryId, string name, string price = "", string description = "")
    {
        if (categoryId is { } id) Category(menu, id);   // must exist
        var item = new MenuItem
        {
            Name = name,
            Price = price,
            Description = description,
            CategoryId = categoryId,
            SortOrder = MenuOrdering.Items(menu, categoryId).Count,
        };
        menu.Items.Add(item);
        return item;
    }

    public static void RemoveItem(Menu menu, Guid itemId)
    {
        var item = Item(menu, itemId);
        var group = MenuOrdering.EffectiveCategoryId(menu, item);
        menu.Items.Remove(item);
        var n = 0;
        foreach (var it in MenuOrdering.Items(menu, group)) it.SortOrder = n++;
    }

    /// <summary>Moves an item to a zero-based position within its current category (clamped to the ends).</summary>
    public static void MoveItem(Menu menu, Guid itemId, int newIndex)
    {
        var item = Item(menu, itemId);
        Place(menu, item, MenuOrdering.EffectiveCategoryId(menu, item), newIndex);
    }

    /// <summary>Moves an item to another category (null = uncategorized) at a zero-based position there.</summary>
    public static void MoveItemToCategory(Menu menu, Guid itemId, Guid? categoryId, int newIndex)
    {
        var item = Item(menu, itemId);
        if (categoryId is { } id) Category(menu, id);
        var from = MenuOrdering.EffectiveCategoryId(menu, item);
        Place(menu, item, categoryId, newIndex);
        if (from != categoryId)
        {
            var n = 0;
            foreach (var it in MenuOrdering.Items(menu, from)) it.SortOrder = n++;
        }
    }

    private static void Place(Menu menu, MenuItem item, Guid? target, int newIndex)
    {
        var list = MenuOrdering.Items(menu, target).ToList();
        list.Remove(item);
        list.Insert(Math.Clamp(newIndex, 0, list.Count), item);
        item.CategoryId = target;
        for (var i = 0; i < list.Count; i++) list[i].SortOrder = i;
    }

    /// <summary>Hidden removes the item from the rendered menu. Independent of sold out.</summary>
    public static void SetVisible(Menu menu, Guid itemId, bool visible) => Item(menu, itemId).Visible = visible;

    /// <summary>Sold out keeps the item visible but unavailable. Independent of hidden.</summary>
    public static void SetSoldOut(Menu menu, Guid itemId, bool soldOut) => Item(menu, itemId).SoldOut = soldOut;

    public static bool ToggleSoldOut(Menu menu, Guid itemId)
    {
        var item = Item(menu, itemId);
        item.SoldOut = !item.SoldOut;
        return item.SoldOut;
    }

    public static void SetFeatured(Menu menu, Guid itemId, bool featured) => Item(menu, itemId).Featured = featured;

    // ---- lookups ----

    private static MenuCategory Category(Menu menu, Guid id) =>
        menu.Categories.FirstOrDefault(c => c.Id == id) ?? throw new ArgumentException($"No category with id {id}.", nameof(id));

    private static MenuItem Item(Menu menu, Guid id) =>
        menu.Items.FirstOrDefault(i => i.Id == id) ?? throw new ArgumentException($"No item with id {id}.", nameof(id));
}
