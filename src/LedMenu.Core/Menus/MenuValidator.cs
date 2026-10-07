namespace LedMenu.Core.Menus;

public enum MenuIssueCode { EmptyName, DuplicateCategoryName, ItemInMissingCategory, LogoMissing, SchemaNewer }

public sealed record MenuIssue(MenuIssueCode Code, string Message);

public static class MenuValidator
{
    /// <summary>
    /// Structural check used when reading or writing a menu file. A file that fails this is treated as damaged
    /// and recovered from a backup. Anything that is merely questionable (missing logo, an item pointing at a
    /// missing category) is NOT rejected here: the data is kept and reported by <see cref="Issues"/>.
    /// </summary>
    public static string? ValidateFile(Menu? menu)
    {
        if (menu is null) return "Menu file is empty.";
        if (menu.SchemaVersion < 1) return $"Invalid schema version {menu.SchemaVersion}.";
        if (menu.SchemaVersion > Menu.CurrentSchemaVersion)
            return $"Menu was written by a newer version (schema {menu.SchemaVersion}).";
        if (menu.Id == Guid.Empty) return "Menu has no id.";
        if (menu.Categories is null || menu.Items is null || menu.Theme is null) return "Menu is missing its categories, items or theme.";
        if (menu.Categories.Any(c => c is null) || menu.Items.Any(i => i is null)) return "Menu contains an empty entry.";

        var dupCat = menu.Categories.GroupBy(c => c.Id).FirstOrDefault(g => g.Count() > 1);
        if (dupCat != null) return $"Two categories share the id {dupCat.Key}.";
        var dupItem = menu.Items.GroupBy(i => i.Id).FirstOrDefault(g => g.Count() > 1);
        if (dupItem != null) return $"Two items share the id {dupItem.Key}.";
        return null;
    }

    /// <summary>Things the operator should know about, without changing or rejecting the menu.</summary>
    public static IReadOnlyList<MenuIssue> Issues(Menu menu, Func<string, bool>? assetExists = null)
    {
        var issues = new List<MenuIssue>();
        if (string.IsNullOrWhiteSpace(menu.Name))
            issues.Add(new(MenuIssueCode.EmptyName, "This menu has no name."));

        foreach (var g in menu.Categories.Where(c => !string.IsNullOrWhiteSpace(c.Name))
                                         .GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                                         .Where(g => g.Count() > 1))
            issues.Add(new(MenuIssueCode.DuplicateCategoryName, $"More than one category is named \"{g.Key}\"."));

        var missing = menu.Items.Count(i => i.CategoryId is { } id && menu.Categories.All(c => c.Id != id));
        if (missing > 0)
            issues.Add(new(MenuIssueCode.ItemInMissingCategory,
                $"{missing} item(s) refer to a category that no longer exists; they are shown as uncategorized."));

        if (!string.IsNullOrWhiteSpace(menu.LogoAsset) && assetExists != null && !assetExists(menu.LogoAsset))
            issues.Add(new(MenuIssueCode.LogoMissing, $"The logo file \"{menu.LogoAsset}\" is missing. The text header is used instead."));

        return issues;
    }
}
