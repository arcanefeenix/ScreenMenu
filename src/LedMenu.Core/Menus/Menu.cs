namespace LedMenu.Core.Menus;

/// <summary>
/// Structured menu content. It says nothing about where it is shown: a menu is shown on a screen by the
/// screen pointing at its Id, so the same menu can move between screens without touching either.
/// The template decides all positioning; the menu holds only data.
/// </summary>
public sealed class Menu
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The operator's name for the menu, e.g. "Musikfest Food Menu". Not necessarily what the audience sees.</summary>
    public string Name { get; set; } = "";

    /// <summary>The heading shown on the LED wall. Falls back to <see cref="Name"/> when empty.</summary>
    public string HeaderText { get; set; } = "";
    public string? Subtitle { get; set; }

    /// <summary>File name (not a path) of an imported logo in the application's assets folder.</summary>
    public string? LogoAsset { get; set; }

    public List<MenuCategory> Categories { get; set; } = new();
    public List<MenuItem> Items { get; set; } = new();
    public MenuTheme Theme { get; set; } = new();
}

public sealed class MenuCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public bool Visible { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class MenuItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Free text on purpose: "$12", "$12 / $16", "Market Price", "3 for $10".</summary>
    public string Price { get; set; } = "";

    /// <summary>Null means uncategorized. An id that matches no category is treated the same way (and reported).</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>False removes the item from the rendered menu entirely (it reflows).</summary>
    public bool Visible { get; set; } = true;

    /// <summary>True keeps the item visible but marked unavailable. How that looks is the template's decision.</summary>
    public bool SoldOut { get; set; }
    public bool Featured { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Template and appearance settings. Only choices, never positions.</summary>
public sealed class MenuTheme
{
    public const int DefaultPageSeconds = 10;

    public string TemplateId { get; set; } = "portrait-basic";

    /// <summary>Font family name from the bundled fonts folder; empty means the default font.</summary>
    public string FontFamily { get; set; } = "";

    /// <summary>How long each page is shown when a menu needs more than one page.</summary>
    public int PageSeconds { get; set; } = DefaultPageSeconds;
    public bool ShowPageIndicator { get; set; } = true;
}
