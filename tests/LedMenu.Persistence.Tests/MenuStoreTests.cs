using LedMenu.Core.Menus;

namespace LedMenu.Persistence.Tests;

public class FileMenuStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ledmenu-test-" + Guid.NewGuid().ToString("N"));
    private AppPaths Paths => new(_root);

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private FileMenuStore Store()
    {
        Paths.EnsureCreated();
        return new FileMenuStore(Paths);
    }

    [Fact]
    public void Menu_round_trips_with_every_field()
    {
        var menu = SampleMenus.FestivalFood();
        menu.Subtitle = "Fresh • Local";
        menu.LogoAsset = "abc123-logo.png";
        menu.Theme.FontFamily = "Barlow";
        menu.Theme.PageSeconds = 12;
        menu.Theme.ShowPageIndicator = false;
        var sold = menu.Items[4];
        sold.SoldOut = true; sold.Visible = false; sold.Featured = true;
        menu.Categories[1].Visible = false;

        Store().Save(menu);
        var back = Store().LoadAll();

        Assert.Empty(back.Issues);
        var m = Assert.Single(back.Menus);
        Assert.Equal(menu.Id, m.Id);
        Assert.Equal((menu.Name, menu.HeaderText, menu.Subtitle, menu.LogoAsset), (m.Name, m.HeaderText, m.Subtitle, m.LogoAsset));
        Assert.Equal(("portrait-basic", "Barlow", 12, false),
            (m.Theme.TemplateId, m.Theme.FontFamily, m.Theme.PageSeconds, m.Theme.ShowPageIndicator));
        Assert.Equal(menu.Categories.Select(c => (c.Id, c.Name, c.Visible, c.SortOrder)),
                     m.Categories.Select(c => (c.Id, c.Name, c.Visible, c.SortOrder)));
        Assert.Equal(menu.Items.Select(i => (i.Id, i.Name, i.Description, i.Price, i.CategoryId, i.Visible, i.SoldOut, i.Featured, i.SortOrder)),
                     m.Items.Select(i => (i.Id, i.Name, i.Description, i.Price, i.CategoryId, i.Visible, i.SoldOut, i.Featured, i.SortOrder)));
        Assert.Equal(Menu.CurrentSchemaVersion, m.SchemaVersion);
    }

    [Fact]
    public void Prices_and_text_keep_their_exact_characters()
    {
        var menu = new Menu { Name = "T" };
        MenuEditor.AddItem(menu, null, "Jalapeño Poppers — \"hot\"", "$12 / $16", "Bacon • Blue Cheese");
        MenuEditor.AddItem(menu, null, "Market", "Market Price");
        MenuEditor.AddItem(menu, null, "Deal", "3 for $10");
        Store().Save(menu);
        var m = Store().LoadAll().Menus.Single();
        Assert.Equal(new[] { "$12 / $16", "Market Price", "3 for $10" }, m.Items.Select(i => i.Price));
        Assert.Equal("Jalapeño Poppers — \"hot\"", m.Items[0].Name);
        Assert.Equal("Bacon • Blue Cheese", m.Items[0].Description);
    }

    [Fact]
    public void Each_menu_is_its_own_file_named_by_id()
    {
        var store = Store();
        var a = new Menu { Name = "A" };
        var b = new Menu { Name = "B" };
        store.Save(a); store.Save(b);
        Assert.True(File.Exists(Path.Combine(Paths.Menus, a.Id.ToString("D") + ".json")));
        Assert.True(File.Exists(Path.Combine(Paths.Menus, b.Id.ToString("D") + ".json")));
        Assert.Equal(2, Store().LoadAll().Menus.Count);
    }

    [Fact]
    public void Empty_folder_loads_nothing_without_error()
    {
        var r = Store().LoadAll();
        Assert.Empty(r.Menus);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Edits_are_saved_and_the_previous_version_is_backed_up()
    {
        var store = Store();
        var m = new Menu { Name = "A" };
        store.Save(m);
        m.Name = "A edited";
        store.Save(m);
        Assert.Equal("A edited", Store().LoadAll().Menus.Single().Name);
        Assert.Single(Directory.GetFiles(Paths.Backups, m.Id.ToString("D") + ".*.json"));
    }

    [Fact]
    public void One_damaged_menu_is_restored_from_backup_and_the_others_are_untouched()
    {
        var store = Store();
        var good = new Menu { Name = "Good" };
        var hurt = new Menu { Name = "Hurt" };
        store.Save(good); store.Save(hurt);
        hurt.Name = "Hurt v2";
        store.Save(hurt);                     // creates a backup of the first version
        File.WriteAllText(Path.Combine(Paths.Menus, hurt.Id.ToString("D") + ".json"), "{ not json");

        var r = Store().LoadAll();

        Assert.Equal(2, r.Menus.Count);
        Assert.Contains(r.Menus, m => m.Name == "Good");
        Assert.Contains(r.Menus, m => m.Id == hurt.Id && m.Name == "Hurt");   // last good backup
        var issue = Assert.Single(r.Issues);
        Assert.True(issue.Recovered);
        Assert.Single(Directory.GetFiles(Paths.Menus, "*.corrupt-*"));         // damaged copy kept
    }

    [Fact]
    public void Damaged_menu_without_a_backup_is_reported_not_replaced_by_an_empty_one()
    {
        var store = Store();
        var ok = new Menu { Name = "Fine" };
        store.Save(ok);
        var badId = Guid.NewGuid();
        File.WriteAllText(Path.Combine(Paths.Menus, badId.ToString("D") + ".json"), "garbage");

        var r = Store().LoadAll();

        Assert.Equal("Fine", Assert.Single(r.Menus).Name);
        var issue = Assert.Single(r.Issues);
        Assert.False(issue.Recovered);
        Assert.Contains(badId.ToString("D"), issue.File);
        Assert.Single(Directory.GetFiles(Paths.Menus, "*.corrupt-*"));
    }

    [Fact]
    public void Structurally_invalid_menu_counts_as_damaged()
    {
        Store();
        File.WriteAllText(Path.Combine(Paths.Menus, Guid.NewGuid().ToString("D") + ".json"),
            "{ \"SchemaVersion\": 1, \"Id\": \"" + Guid.NewGuid() + "\", \"Name\": \"x\", \"Categories\": [], \"Items\": [ { \"Id\": \"11111111-1111-1111-1111-111111111111\" }, { \"Id\": \"11111111-1111-1111-1111-111111111111\" } ], \"Theme\": {} }");
        var r = Store().LoadAll();
        Assert.Empty(r.Menus);
        Assert.Single(r.Issues);
    }

    [Fact]
    public void Menu_from_a_newer_version_is_not_loaded_or_overwritten()
    {
        Store();
        var path = Path.Combine(Paths.Menus, Guid.NewGuid().ToString("D") + ".json");
        File.WriteAllText(path, "{ \"SchemaVersion\": 99, \"Id\": \"" + Guid.NewGuid() + "\", \"Name\": \"future\" }");
        var r = Store().LoadAll();
        Assert.Empty(r.Menus);
        Assert.Single(r.Issues);
        Assert.Contains("future", Directory.GetFiles(Paths.Menus, "*.corrupt-*").Select(File.ReadAllText).Single());
    }

    [Fact]
    public void Delete_moves_the_file_to_trash_and_the_menu_does_not_come_back()
    {
        var store = Store();
        var m = new Menu { Name = "Gone" };
        store.Save(m);
        store.Save(m);   // so a backup exists too
        store.Delete(m);

        Assert.False(File.Exists(Path.Combine(Paths.Menus, m.Id.ToString("D") + ".json")));
        var trashed = Assert.Single(Directory.GetFiles(Paths.Trash));
        Assert.Contains("Gone", File.ReadAllText(trashed));
        Assert.Empty(Store().LoadAll().Menus);
    }

    [Fact]
    public void Deleting_the_same_menu_name_twice_never_overwrites_the_trash_copy()
    {
        var store = Store();
        var m = new Menu { Name = "Again" };
        store.Save(m); store.Delete(m);
        store.Save(m); store.Delete(m);
        Assert.Equal(2, Directory.GetFiles(Paths.Trash).Length);
    }

    [Fact]
    public void A_leftover_temp_file_from_a_power_cut_does_not_hide_the_menu()
    {
        var store = Store();
        var m = new Menu { Name = "Safe" };
        store.Save(m);
        File.WriteAllText(Path.Combine(Paths.Menus, m.Id.ToString("D") + ".json.tmp"), "{ half");
        var r = Store().LoadAll();
        Assert.Equal("Safe", Assert.Single(r.Menus).Name);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void A_file_with_a_different_name_keeps_saving_to_the_same_file()
    {
        Store();
        var m = new Menu { Name = "Renamed file" };
        var odd = Path.Combine(Paths.Menus, "my menu.json");
        File.WriteAllText(odd, System.Text.Json.JsonSerializer.Serialize(m));
        var store = Store();
        var loaded = store.LoadAll().Menus.Single();
        loaded.Name = "Edited";
        store.Save(loaded);
        Assert.Single(Directory.GetFiles(Paths.Menus, "*.json"));
        Assert.Contains("Edited", File.ReadAllText(odd));
    }

    [Fact]
    public void Library_over_the_real_store_survives_a_restart()
    {
        var lib = new MenuLibrary(Store());
        var food = lib.Create("Food");
        MenuEditor.AddItem(food, null, "Fries", "$6");
        lib.Save(food);
        lib.Create("Drinks");

        var lib2 = new MenuLibrary(Store());
        lib2.Load();
        Assert.Equal(new[] { "Drinks", "Food" }, lib2.Menus.Select(m => m.Name));
        Assert.Equal("Fries", lib2.Find(food.Id)!.Items.Single().Name);
        lib2.Delete(food.Id);
        var lib3 = new MenuLibrary(Store());
        lib3.Load();
        Assert.Equal(new[] { "Drinks" }, lib3.Menus.Select(m => m.Name));
    }
}

public class AssetStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ledmenu-test-" + Guid.NewGuid().ToString("N"));
    private string AssetsDir => Path.Combine(_root, "assets");
    private string Src(string name) => Path.Combine(_root, "source", name);

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string WriteSource(string name, byte[] bytes)
    {
        Directory.CreateDirectory(Path.Combine(_root, "source"));
        File.WriteAllBytes(Src(name), bytes);
        return Src(name);
    }

    private static byte[] Png(int extra = 20, byte fill = 7)
    {
        var b = new byte[8 + extra];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
        for (var i = 8; i < b.Length; i++) b[i] = fill;
        return b;
    }

    [Fact]
    public void Import_copies_the_file_into_the_assets_folder()
    {
        var store = new AssetStore(AssetsDir);
        var src = WriteSource("My Logo (final).PNG", Png());
        var r = store.Import(src);
        Assert.False(r.AlreadyPresent);
        Assert.EndsWith(".png", r.FileName);
        Assert.Contains("My_Logo", r.FileName);
        Assert.True(File.Exists(Path.Combine(AssetsDir, r.FileName)));
        Assert.Equal(File.ReadAllBytes(src), File.ReadAllBytes(Path.Combine(AssetsDir, r.FileName)));
    }

    [Fact]
    public void Moving_or_deleting_the_original_does_not_break_the_asset()
    {
        var store = new AssetStore(AssetsDir);
        var src = WriteSource("logo.png", Png());
        var name = store.Import(src).FileName;
        File.Delete(src);
        Assert.True(store.Exists(name));
        Assert.NotNull(store.Resolve(name));
    }

    [Fact]
    public void Importing_the_same_picture_twice_reuses_the_copy()
    {
        var store = new AssetStore(AssetsDir);
        var a = store.Import(WriteSource("one.png", Png()));
        var b = store.Import(WriteSource("copy-of-one.png", Png()));
        Assert.Equal(a.FileName, b.FileName);
        Assert.True(b.AlreadyPresent);
        Assert.Single(Directory.GetFiles(AssetsDir));
    }

    [Fact]
    public void Same_name_but_different_content_gets_a_different_asset()
    {
        var store = new AssetStore(AssetsDir);
        var a = store.Import(WriteSource("logo.png", Png(fill: 1)));
        File.Delete(Src("logo.png"));
        var b = store.Import(WriteSource("logo.png", Png(fill: 2)));
        Assert.NotEqual(a.FileName, b.FileName);
        Assert.True(store.Exists(a.FileName) && store.Exists(b.FileName));
    }

    [Fact]
    public void File_type_comes_from_the_content_not_the_extension()
    {
        var store = new AssetStore(AssetsDir);
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 };
        var r = store.Import(WriteSource("photo.png", jpeg));   // lies about being a PNG
        Assert.EndsWith(".jpg", r.FileName);
    }

    [Fact]
    public void Non_images_missing_files_and_empty_files_are_refused_with_a_message()
    {
        var store = new AssetStore(AssetsDir);
        var text = Assert.Throws<AssetImportException>(() => store.Import(WriteSource("note.png", "hello"u8.ToArray())));
        Assert.Contains("not a PNG", text.Message);
        var missing = Assert.Throws<AssetImportException>(() => store.Import(Src("nope.png")));
        Assert.Contains("does not exist", missing.Message);
        var empty = Assert.Throws<AssetImportException>(() => store.Import(WriteSource("empty.png", Array.Empty<byte>())));
        Assert.Contains("empty", empty.Message);
        Assert.False(Directory.Exists(AssetsDir) && Directory.GetFiles(AssetsDir).Length > 0);
    }

    [Fact]
    public void Resolve_refuses_names_that_could_escape_the_folder()
    {
        var store = new AssetStore(AssetsDir);
        Directory.CreateDirectory(AssetsDir);
        File.WriteAllBytes(Path.Combine(_root, "secret.png"), Png());
        Assert.Null(store.Resolve("..\\secret.png"));
        Assert.Null(store.Resolve("../secret.png"));
        Assert.Null(store.Resolve(Path.Combine(_root, "secret.png")));
        Assert.Null(store.Resolve(null));
        Assert.Null(store.Resolve("missing.png"));
        Assert.False(store.Exists("missing.png"));
    }

    [Fact]
    public void Missing_logo_is_detected_by_the_menu_check()
    {
        var store = new AssetStore(AssetsDir);
        var name = store.Import(WriteSource("logo.png", Png())).FileName;
        var menu = new Menu { Name = "x", LogoAsset = name };
        Assert.DoesNotContain(MenuValidator.Issues(menu, store.Exists), i => i.Code == MenuIssueCode.LogoMissing);
        File.Delete(Path.Combine(AssetsDir, name));
        Assert.Contains(MenuValidator.Issues(menu, store.Exists), i => i.Code == MenuIssueCode.LogoMissing);
    }
}
