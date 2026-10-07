using LedMenu.Core.Assets;
using LedMenu.Core.Menus;
using LedMenu.Core.Screens;

namespace LedMenu.Core.Tests;

public class MenuOrderingTests
{
    private static Menu M() => new() { Name = "Test" };

    [Fact]
    public void Categories_sort_by_sort_order_then_by_stored_position()
    {
        var m = M();
        var a = new MenuCategory { Name = "A", SortOrder = 5 };
        var b = new MenuCategory { Name = "B", SortOrder = 1 };
        var c = new MenuCategory { Name = "C", SortOrder = 5 };   // ties with A: A came first in the list
        m.Categories.AddRange(new[] { a, b, c });
        Assert.Equal(new[] { "B", "A", "C" }, MenuOrdering.Categories(m).Select(x => x.Name));
    }

    [Fact]
    public void Items_are_ordered_within_their_own_category_only()
    {
        var m = M();
        var burgers = MenuEditor.AddCategory(m, "Burgers");
        var sides = MenuEditor.AddCategory(m, "Sides");
        MenuEditor.AddItem(m, burgers.Id, "B1");
        MenuEditor.AddItem(m, sides.Id, "S1");
        MenuEditor.AddItem(m, burgers.Id, "B2");
        MenuEditor.AddItem(m, null, "Loose");
        Assert.Equal(new[] { "B1", "B2" }, MenuOrdering.Items(m, burgers.Id).Select(i => i.Name));
        Assert.Equal(new[] { "S1" }, MenuOrdering.Items(m, sides.Id).Select(i => i.Name));
        Assert.Equal(new[] { "Loose" }, MenuOrdering.Items(m, null).Select(i => i.Name));
    }

    [Fact]
    public void Ordering_is_deterministic_for_equal_sort_orders()
    {
        var m = M();
        var cat = MenuEditor.AddCategory(m, "C");
        foreach (var n in new[] { "x", "y", "z" })
            m.Items.Add(new MenuItem { Name = n, CategoryId = cat.Id, SortOrder = 0 });
        Assert.Equal(new[] { "x", "y", "z" }, MenuOrdering.Items(m, cat.Id).Select(i => i.Name));
        Assert.Equal(new[] { "x", "y", "z" }, MenuOrdering.Items(m, cat.Id).Select(i => i.Name));
    }

    [Fact]
    public void Item_pointing_at_a_missing_category_counts_as_uncategorized()
    {
        var m = M();
        m.Items.Add(new MenuItem { Name = "Orphan", CategoryId = Guid.NewGuid() });
        Assert.Equal(new[] { "Orphan" }, MenuOrdering.Items(m, null).Select(i => i.Name));
        Assert.Null(MenuOrdering.EffectiveCategoryId(m, m.Items[0]));
    }

    [Fact]
    public void Normalize_renumbers_from_zero_without_changing_the_order()
    {
        var m = M();
        var a = new MenuCategory { Name = "A", SortOrder = 40 };
        var b = new MenuCategory { Name = "B", SortOrder = 7 };
        m.Categories.AddRange(new[] { a, b });
        m.Items.Add(new MenuItem { Name = "i2", CategoryId = a.Id, SortOrder = 90 });
        m.Items.Add(new MenuItem { Name = "i1", CategoryId = a.Id, SortOrder = 10 });
        MenuOrdering.Normalize(m);
        Assert.Equal((1, 0), (a.SortOrder, b.SortOrder));
        Assert.Equal(new[] { "i1", "i2" }, MenuOrdering.Items(m, a.Id).Select(i => i.Name));
        Assert.Equal(new[] { 0, 1 }, MenuOrdering.Items(m, a.Id).Select(i => i.SortOrder));
    }
}

public class MenuEditorTests
{
    private static (Menu menu, MenuCategory burgers, MenuCategory sides) Setup()
    {
        var m = new Menu { Name = "T" };
        var b = MenuEditor.AddCategory(m, "Burgers");
        var s = MenuEditor.AddCategory(m, "Sides");
        MenuEditor.AddItem(m, b.Id, "B1", "$1");
        MenuEditor.AddItem(m, b.Id, "B2", "$2");
        MenuEditor.AddItem(m, b.Id, "B3", "$3");
        MenuEditor.AddItem(m, s.Id, "S1", "$4");
        return (m, b, s);
    }

    private static string[] Names(Menu m, Guid? cat) => MenuOrdering.Items(m, cat).Select(i => i.Name).ToArray();

    [Fact]
    public void New_categories_and_items_go_to_the_end()
    {
        var (m, b, s) = Setup();
        var extra = MenuEditor.AddCategory(m, "Drinks");
        Assert.Equal(new[] { "Burgers", "Sides", "Drinks" }, MenuOrdering.Categories(m).Select(c => c.Name));
        MenuEditor.AddItem(m, b.Id, "B4");
        Assert.Equal(new[] { "B1", "B2", "B3", "B4" }, Names(m, b.Id));
        Assert.Equal(2, extra.SortOrder);
    }

    [Fact]
    public void Adding_to_an_unknown_category_is_refused()
    {
        var (m, _, _) = Setup();
        Assert.Throws<ArgumentException>(() => MenuEditor.AddItem(m, Guid.NewGuid(), "X"));
    }

    [Theory]
    [InlineData(0, new[] { "B3", "B1", "B2" }, 2)]
    [InlineData(1, new[] { "B1", "B3", "B2" }, 2)]
    [InlineData(99, new[] { "B1", "B2", "B3" }, 2)]
    [InlineData(-5, new[] { "B3", "B1", "B2" }, 2)]
    public void Moving_an_item_reorders_within_its_category(int newIndex, string[] expected, int moveIdx)
    {
        var (m, b, _) = Setup();
        var id = MenuOrdering.Items(m, b.Id)[moveIdx].Id;
        MenuEditor.MoveItem(m, id, newIndex);
        Assert.Equal(expected, Names(m, b.Id));
        Assert.Equal(new[] { 0, 1, 2 }, MenuOrdering.Items(m, b.Id).Select(i => i.SortOrder));
    }

    [Fact]
    public void Moving_an_item_to_another_category_keeps_both_sequences_clean()
    {
        var (m, b, s) = Setup();
        var b1 = MenuOrdering.Items(m, b.Id)[0];
        MenuEditor.MoveItemToCategory(m, b1.Id, s.Id, 0);
        Assert.Equal(new[] { "B2", "B3" }, Names(m, b.Id));
        Assert.Equal(new[] { "B1", "S1" }, Names(m, s.Id));
        Assert.Equal(new[] { 0, 1 }, MenuOrdering.Items(m, b.Id).Select(i => i.SortOrder));
        Assert.Equal(new[] { 0, 1 }, MenuOrdering.Items(m, s.Id).Select(i => i.SortOrder));
        MenuEditor.MoveItemToCategory(m, b1.Id, null, 0);
        Assert.Equal(new[] { "B1" }, Names(m, null));
    }

    [Fact]
    public void Moving_a_category_reorders_the_list()
    {
        var (m, b, s) = Setup();
        MenuEditor.MoveCategory(m, s.Id, 0);
        Assert.Equal(new[] { "Sides", "Burgers" }, MenuOrdering.Categories(m).Select(c => c.Name));
        MenuEditor.MoveCategory(m, s.Id, 50);
        Assert.Equal(new[] { "Burgers", "Sides" }, MenuOrdering.Categories(m).Select(c => c.Name));
    }

    [Fact]
    public void Removing_an_item_closes_the_gap()
    {
        var (m, b, _) = Setup();
        MenuEditor.RemoveItem(m, MenuOrdering.Items(m, b.Id)[1].Id);
        Assert.Equal(new[] { "B1", "B3" }, Names(m, b.Id));
        Assert.Equal(new[] { 0, 1 }, MenuOrdering.Items(m, b.Id).Select(i => i.SortOrder));
    }

    [Fact]
    public void Removing_a_category_can_keep_its_items_as_uncategorized()
    {
        var (m, b, _) = Setup();
        MenuEditor.RemoveCategory(m, b.Id, deleteItems: false);
        Assert.Equal(new[] { "Sides" }, MenuOrdering.Categories(m).Select(c => c.Name));
        Assert.Equal(new[] { "B1", "B2", "B3" }, Names(m, null));
        Assert.All(m.Items, i => Assert.True(i.CategoryId is null || m.Categories.Any(c => c.Id == i.CategoryId)));
    }

    [Fact]
    public void Removing_a_category_can_delete_its_items()
    {
        var (m, b, _) = Setup();
        MenuEditor.RemoveCategory(m, b.Id, deleteItems: true);
        Assert.Equal(new[] { "S1" }, m.Items.Select(i => i.Name));
    }

    [Fact]
    public void Unknown_ids_throw_instead_of_silently_doing_nothing()
    {
        var (m, _, _) = Setup();
        Assert.Throws<ArgumentException>(() => MenuEditor.RemoveItem(m, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => MenuEditor.SetSoldOut(m, Guid.NewGuid(), true));
        Assert.Throws<ArgumentException>(() => MenuEditor.MoveCategory(m, Guid.NewGuid(), 0));
        Assert.Throws<ArgumentException>(() => MenuEditor.RemoveCategory(m, Guid.NewGuid(), false));
    }

    [Fact]
    public void Sold_out_and_hidden_are_independent_and_featured_is_separate()
    {
        var (m, b, _) = Setup();
        var item = MenuOrdering.Items(m, b.Id)[0];
        MenuEditor.SetSoldOut(m, item.Id, true);
        Assert.True(item.SoldOut);
        Assert.True(item.Visible);
        MenuEditor.SetVisible(m, item.Id, false);
        Assert.True(item.SoldOut);
        Assert.False(item.Visible);
        MenuEditor.SetSoldOut(m, item.Id, false);
        Assert.False(item.Visible);
        MenuEditor.SetFeatured(m, item.Id, true);
        Assert.True(item.Featured);
        Assert.False(item.SoldOut);
        Assert.True(MenuEditor.ToggleSoldOut(m, item.Id));
        Assert.True(item.SoldOut);
    }
}

public class MenuViewTests
{
    private static Menu Sample() => SampleMenus.FestivalFood();

    [Fact]
    public void Visible_items_appear_in_category_order()
    {
        var v = MenuView.Build(Sample());
        Assert.Equal(new[] { "Burgers", "Appetizers", "Sides" }, v.Groups.Select(g => g.Category!.Name));
        Assert.Equal(new[] { "Classic Burger", "Black & Blue Burger", "Veggie Burger" }, v.Groups[0].Items.Select(i => i.Name));
        Assert.Equal(10, v.ItemCount);
        Assert.False(v.IsEmpty);
    }

    [Fact]
    public void Hidden_items_disappear_and_the_rest_reflow()
    {
        var m = Sample();
        MenuEditor.SetVisible(m, m.Items.First(i => i.Name == "Black & Blue Burger").Id, false);
        var burgers = MenuView.Build(m).Groups[0];
        Assert.Equal(new[] { "Classic Burger", "Veggie Burger" }, burgers.Items.Select(i => i.Name));
    }

    [Fact]
    public void Unhiding_restores_the_original_position()
    {
        var m = Sample();
        var id = m.Items.First(i => i.Name == "Black & Blue Burger").Id;
        MenuEditor.SetVisible(m, id, false);
        MenuEditor.SetVisible(m, id, true);
        Assert.Equal(new[] { "Classic Burger", "Black & Blue Burger", "Veggie Burger" }, MenuView.Build(m).Groups[0].Items.Select(i => i.Name));
    }

    [Fact]
    public void Sold_out_items_stay_in_place_and_stay_flagged()
    {
        var m = Sample();
        MenuEditor.SetSoldOut(m, m.Items.First(i => i.Name == "Fries").Id, true);
        var sides = MenuView.Build(m).Groups.Single(g => g.Category!.Name == "Sides");
        Assert.Equal(new[] { "Fries", "Onion Rings", "Coleslaw", "Daily Special" }, sides.Items.Select(i => i.Name));
        Assert.True(sides.Items[0].SoldOut);
    }

    [Fact]
    public void A_hidden_category_hides_all_its_items_without_touching_them()
    {
        var m = Sample();
        var apps = m.Categories.First(c => c.Name == "Appetizers");
        MenuEditor.SetCategoryVisible(m, apps.Id, false);
        var v = MenuView.Build(m);
        Assert.DoesNotContain(v.Groups, g => g.Category?.Name == "Appetizers");
        Assert.Equal(7, v.ItemCount);
        Assert.All(m.Items.Where(i => i.CategoryId == apps.Id), i => Assert.True(i.Visible));
    }

    [Fact]
    public void A_category_with_no_visible_items_shows_no_header()
    {
        var m = Sample();
        foreach (var i in m.Items.Where(i => i.CategoryId == m.Categories.First(c => c.Name == "Sides").Id))
            i.Visible = false;
        Assert.DoesNotContain(MenuView.Build(m).Groups, g => g.Category?.Name == "Sides");
    }

    [Fact]
    public void Empty_menu_is_empty_but_still_has_its_header()
    {
        var v = MenuView.Build(new Menu { Name = "Nothing yet", HeaderText = "" });
        Assert.True(v.IsEmpty);
        Assert.Equal("Nothing yet", v.Header);
        Assert.Empty(v.Groups);
    }

    [Fact]
    public void All_items_hidden_gives_an_empty_view()
    {
        var m = Sample();
        foreach (var i in m.Items) i.Visible = false;
        Assert.True(MenuView.Build(m).IsEmpty);
    }

    [Fact]
    public void All_items_sold_out_are_all_still_shown()
    {
        var m = Sample();
        foreach (var i in m.Items) i.SoldOut = true;
        var v = MenuView.Build(m);
        Assert.Equal(10, v.ItemCount);
        Assert.All(v.Groups.SelectMany(g => g.Items), i => Assert.True(i.SoldOut));
    }

    [Fact]
    public void Uncategorized_items_follow_the_categories()
    {
        var m = Sample();
        MenuEditor.AddItem(m, null, "Water", "Free");
        var v = MenuView.Build(m);
        Assert.Null(v.Groups[^1].Category);
        Assert.Equal(new[] { "Water" }, v.Groups[^1].Items.Select(i => i.Name));
    }

    [Fact]
    public void Header_falls_back_to_the_menu_name_and_blank_subtitle_is_dropped()
    {
        var m = new Menu { Name = "Garlic Fest Menu", HeaderText = "  ", Subtitle = "   " };
        var v = MenuView.Build(m);
        Assert.Equal("Garlic Fest Menu", v.Header);
        Assert.Null(v.Subtitle);
        m.HeaderText = "GARLIC FEST";
        Assert.Equal("GARLIC FEST", MenuView.Build(m).Header);
    }

    [Fact]
    public void Price_is_free_text_and_not_interpreted()
    {
        var m = Sample();
        var prices = MenuView.Build(m).Groups.SelectMany(g => g.Items).Select(i => i.Price).ToList();
        Assert.Contains("$6 / $9", prices);
        Assert.Contains("3 for $10", prices);
        Assert.Contains("Market Price", prices);
    }

    [Fact]
    public void View_does_not_change_the_menu()
    {
        var m = Sample();
        var before = System.Text.Json.JsonSerializer.Serialize(m);
        _ = MenuView.Build(m);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(m));
    }

    [Fact]
    public void Sample_menu_is_valid_and_complete()
    {
        var m = Sample();
        Assert.Null(MenuValidator.ValidateFile(m));
        Assert.Empty(MenuValidator.Issues(m));
        Assert.Equal(3, m.Categories.Count);
        Assert.Equal(10, m.Items.Count);
        Assert.Single(m.Items, i => i.Featured);
    }
}

public class MenuValidatorTests
{
    [Fact]
    public void A_normal_menu_is_a_valid_file() => Assert.Null(MenuValidator.ValidateFile(new Menu { Name = "x" }));

    [Fact]
    public void Structural_problems_make_the_file_invalid()
    {
        Assert.NotNull(MenuValidator.ValidateFile(null));
        Assert.NotNull(MenuValidator.ValidateFile(new Menu { SchemaVersion = 0 }));
        Assert.NotNull(MenuValidator.ValidateFile(new Menu { SchemaVersion = Menu.CurrentSchemaVersion + 1 }));
        Assert.NotNull(MenuValidator.ValidateFile(new Menu { Id = Guid.Empty }));
        Assert.NotNull(MenuValidator.ValidateFile(new Menu { Items = null! }));

        var dupCat = new Menu();
        var id = Guid.NewGuid();
        dupCat.Categories.Add(new MenuCategory { Id = id });
        dupCat.Categories.Add(new MenuCategory { Id = id });
        Assert.NotNull(MenuValidator.ValidateFile(dupCat));

        var dupItem = new Menu();
        dupItem.Items.Add(new MenuItem { Id = id });
        dupItem.Items.Add(new MenuItem { Id = id });
        Assert.NotNull(MenuValidator.ValidateFile(dupItem));
    }

    [Fact]
    public void Questionable_content_is_kept_and_reported_not_rejected()
    {
        var m = new Menu { Name = "", LogoAsset = "gone.png" };
        MenuEditor.AddCategory(m, "Food");
        MenuEditor.AddCategory(m, "food");
        m.Items.Add(new MenuItem { Name = "Orphan", CategoryId = Guid.NewGuid() });
        Assert.Null(MenuValidator.ValidateFile(m));

        var codes = MenuValidator.Issues(m, _ => false).Select(i => i.Code).ToList();
        Assert.Contains(MenuIssueCode.EmptyName, codes);
        Assert.Contains(MenuIssueCode.DuplicateCategoryName, codes);
        Assert.Contains(MenuIssueCode.ItemInMissingCategory, codes);
        Assert.Contains(MenuIssueCode.LogoMissing, codes);
    }

    [Fact]
    public void Present_logo_raises_no_issue()
    {
        var m = new Menu { Name = "x", LogoAsset = "logo.png" };
        Assert.DoesNotContain(MenuValidator.Issues(m, n => n == "logo.png"), i => i.Code == MenuIssueCode.LogoMissing);
    }
}

public class MenuLibraryTests
{
    private sealed class MemoryStore : IMenuStore
    {
        public Dictionary<Guid, Menu> Saved { get; } = new();
        public List<Guid> Deleted { get; } = new();
        public List<Menu> ToLoad { get; } = new();
        public List<MenuLoadIssue> LoadIssues { get; } = new();
        public MenuLoadResult LoadAll() => new(ToLoad.ToList(), LoadIssues.ToList());
        public void Save(Menu menu) => Saved[menu.Id] = menu;
        public void Delete(Menu menu) { Deleted.Add(menu.Id); Saved.Remove(menu.Id); }
    }

    [Fact]
    public void Create_saves_immediately_and_uses_the_name_as_header()
    {
        var store = new MemoryStore();
        var lib = new MenuLibrary(store);
        var m = lib.Create("Musikfest Food Menu");
        Assert.Equal("Musikfest Food Menu", m.HeaderText);
        Assert.Same(m, store.Saved[m.Id]);
        Assert.Contains(m.Id, lib.Ids);
    }

    [Fact]
    public void Menus_list_alphabetically_and_stably()
    {
        var lib = new MenuLibrary(new MemoryStore());
        lib.Create("beta"); lib.Create("Alpha"); lib.Create("Charlie");
        Assert.Equal(new[] { "Alpha", "beta", "Charlie" }, lib.Menus.Select(m => m.Name));
    }

    [Fact]
    public void Load_reads_the_store_and_surfaces_its_issues()
    {
        var store = new MemoryStore();
        store.ToLoad.Add(new Menu { Name = "One" });
        store.LoadIssues.Add(new MenuLoadIssue("bad.json", "damaged", false));
        var lib = new MenuLibrary(store);
        lib.Load();
        Assert.Single(lib.Menus);
        Assert.Single(lib.LoadIssues);
    }

    [Fact]
    public void Duplicate_ids_across_files_are_reported_and_only_one_is_loaded()
    {
        var store = new MemoryStore();
        var id = Guid.NewGuid();
        store.ToLoad.Add(new Menu { Id = id, Name = "First" });
        store.ToLoad.Add(new Menu { Id = id, Name = "Copy" });
        var lib = new MenuLibrary(store);
        lib.Load();
        Assert.Equal("First", Assert.Single(lib.Menus).Name);
        Assert.Contains(lib.LoadIssues, i => i.Message.Contains("same id"));
    }

    [Fact]
    public void Save_writes_the_edited_menu_and_refuses_strangers()
    {
        var store = new MemoryStore();
        var lib = new MenuLibrary(store);
        var m = lib.Create("A");
        MenuEditor.AddCategory(m, "Burgers");
        lib.Save(m);
        Assert.Single(store.Saved[m.Id].Categories);
        Assert.Throws<InvalidOperationException>(() => lib.Save(new Menu { Name = "stranger" }));
    }

    [Fact]
    public void Delete_removes_the_menu_through_the_store_but_edits_no_screen()
    {
        var store = new MemoryStore();
        var lib = new MenuLibrary(store);
        var m = lib.Create("A");
        var screen = new Screen { Name = "S", Width = 10, Height = 10, AssignedMenuId = m.Id };
        lib.Delete(m.Id);
        Assert.Empty(lib.Menus);
        Assert.Equal(new[] { m.Id }, store.Deleted);
        Assert.Equal(m.Id, screen.AssignedMenuId);   // the assignment is left as it was
        Assert.Throws<ArgumentException>(() => lib.Delete(Guid.NewGuid()));
    }

    [Fact]
    public void Adding_the_same_id_twice_is_refused()
    {
        var lib = new MenuLibrary(new MemoryStore());
        var m = new Menu { Name = "A" };
        lib.Add(m);
        Assert.Throws<InvalidOperationException>(() => lib.Add(m));
    }
}

public class MenuAssignmentTests
{
    private static Screen S(Guid? menu, bool enabled = true) =>
        new() { Name = "S", Width = 100, Height = 100, AssignedMenuId = menu, Enabled = enabled };

    [Fact]
    public void Screen_without_a_menu_is_fine()
    {
        Assert.Empty(ScreenValidator.Validate(new[] { S(null) }, new CanvasSize(1920, 1080), new HashSet<Guid>()));
    }

    [Fact]
    public void Assignment_to_an_existing_menu_is_fine()
    {
        var id = Guid.NewGuid();
        Assert.Empty(ScreenValidator.Validate(new[] { S(id) }, new CanvasSize(1920, 1080), new HashSet<Guid> { id }));
    }

    [Fact]
    public void Assignment_to_a_missing_menu_warns_and_is_kept()
    {
        var id = Guid.NewGuid();
        var screen = S(id);
        var issues = ScreenValidator.Validate(new[] { screen }, new CanvasSize(1920, 1080), new HashSet<Guid>());
        var i = Assert.Single(issues);
        Assert.Equal((IssueCode.MissingMenu, IssueSeverity.Warning), (i.Code, i.Severity));
        Assert.Equal(id, screen.AssignedMenuId);
    }

    [Fact]
    public void Missing_menu_does_not_stop_the_screen_from_being_drawn()
    {
        var screen = S(Guid.NewGuid());
        Assert.Single(ScreenValidator.RenderableScreens(new[] { screen }, new CanvasSize(1920, 1080)));
    }

    [Fact]
    public void Unknown_menu_set_means_menus_are_not_checked()
    {
        Assert.Empty(ScreenValidator.Validate(new[] { S(Guid.NewGuid()) }, new CanvasSize(1920, 1080), null));
    }

    [Fact]
    public void Two_screens_may_share_one_menu()
    {
        var id = Guid.NewGuid();
        Assert.Empty(ScreenValidator.Validate(
            new[] { new Screen { Name = "A", X = 0, Width = 100, Height = 100, AssignedMenuId = id },
                    new Screen { Name = "B", X = 100, Width = 100, Height = 100, AssignedMenuId = id } },
            new CanvasSize(1920, 1080), new HashSet<Guid> { id }));
    }
}

public class AssetRulesTests
{
    private static byte[] Bytes(params byte[] b) => b;

    [Fact]
    public void Image_type_is_decided_from_content()
    {
        Assert.Equal(ImageKind.Png, AssetRules.Detect(Bytes(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0)));
        Assert.Equal(ImageKind.Jpeg, AssetRules.Detect(Bytes(0xFF, 0xD8, 0xFF, 0xE0)));
        Assert.Equal(ImageKind.Bmp, AssetRules.Detect(Bytes(0x42, 0x4D, 0, 0)));
        Assert.Equal(ImageKind.Gif, AssetRules.Detect("GIF89a"u8));
        Assert.Equal(ImageKind.Gif, AssetRules.Detect("GIF87a"u8));
        Assert.Null(AssetRules.Detect("MZ"u8));
        Assert.Null(AssetRules.Detect("<svg xmlns"u8));
        Assert.Null(AssetRules.Detect(Array.Empty<byte>()));
    }

    [Theory]
    [InlineData("logo.png", true)]
    [InlineData("a1b2c3-My_Logo.jpg", true)]
    [InlineData("..\\secret.png", false)]
    [InlineData("../secret.png", false)]
    [InlineData("sub/dir.png", false)]
    [InlineData("C:logo.png", false)]
    [InlineData("a..b.png", false)]
    [InlineData("", false)]
    [InlineData(" logo.png", false)]
    [InlineData(null, false)]
    public void Only_plain_file_names_are_safe(string? name, bool safe) => Assert.Equal(safe, AssetRules.IsSafeFileName(name));

    [Fact]
    public void Base_names_are_cleaned_and_never_empty()
    {
        Assert.Equal("My_Logo__v2", AssetRules.SanitizeBaseName("My Logo (v2)"));
        Assert.Equal("image", AssetRules.SanitizeBaseName("???"));
        Assert.Equal("image", AssetRules.SanitizeBaseName(null));
        Assert.True(AssetRules.SanitizeBaseName(new string('a', 500)).Length <= AssetRules.MaxBaseNameLength);
    }

    [Fact]
    public void Built_names_are_safe_and_follow_the_content_hash()
    {
        var name = AssetRules.BuildFileName("ABCDEF0123456789FFFF", "My Logo", ImageKind.Png);
        Assert.Equal("abcdef012345-My_Logo.png", name);
        Assert.True(AssetRules.IsSafeFileName(name));
        Assert.Equal(".jpg", AssetRules.Extension(ImageKind.Jpeg));
    }
}
