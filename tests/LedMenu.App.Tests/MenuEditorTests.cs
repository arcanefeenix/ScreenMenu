using LedMenu.App.ViewModels;
using LedMenu.Core.Autosave;
using LedMenu.Core.Menus;

namespace LedMenu.App.Tests;

internal sealed class FakeClock : IScheduler
{
    private sealed class Entry : IDisposable
    {
        public required TimeSpan Due { get; init; }
        public required Action Callback { get; init; }
        public bool Cancelled { get; set; }
        public void Dispose() => Cancelled = true;
    }

    private readonly List<Entry> _entries = new();
    public TimeSpan Now { get; private set; }

    public IDisposable ScheduleOnce(TimeSpan delay, Action callback)
    {
        var e = new Entry { Due = Now + delay, Callback = callback };
        _entries.Add(e);
        return e;
    }

    public void Advance(TimeSpan by)
    {
        Now += by;
        foreach (var e in _entries.Where(e => !e.Cancelled && e.Due <= Now).ToList())
        {
            e.Cancelled = true;
            e.Callback();
        }
    }
}

internal sealed class MemoryStore : IMenuStore
{
    public Dictionary<Guid, string> SavedJson { get; } = new();
    public int SaveCount { get; private set; }
    public void ResetCount() => SaveCount = 0;
    public bool FailSaves { get; set; }
    public int FailedAttempts { get; private set; }

    public MenuLoadResult LoadAll() => new(Array.Empty<Menu>(), Array.Empty<MenuLoadIssue>());

    public void Save(Menu menu)
    {
        if (FailSaves) { FailedAttempts++; throw new System.IO.IOException("disk full"); }
        SaveCount++;
        SavedJson[menu.Id] = System.Text.Json.JsonSerializer.Serialize(menu);
    }

    public void Delete(Menu menu) => SavedJson.Remove(menu.Id);

    public Menu SavedCopy(Guid id) => System.Text.Json.JsonSerializer.Deserialize<Menu>(SavedJson[id])!;
}

/// <summary>Everything an editor test needs, with answers to the confirmation questions under the test's control.</summary>
internal sealed class EditorRig
{
    public MemoryStore Store { get; } = new();
    public FakeClock Clock { get; } = new();
    public MenuLibrary Library { get; }
    public SaveDebouncer Saver { get; }
    public MenuEditorViewModel Editor { get; }
    public Menu Menu { get; }
    public List<Menu> Notified { get; } = new();
    public bool ConfirmAnswer { get; set; } = true;
    public DeleteCategoryChoice CategoryAnswer { get; set; } = DeleteCategoryChoice.KeepItems;

    public EditorRig(Menu? menu = null)
    {
        Library = new MenuLibrary(Store);
        Menu = menu ?? SampleMenus.FestivalFood();
        Library.Add(Menu);
        Store.SaveCountReset();
        Saver = new SaveDebouncer(Clock);
        Editor = new MenuEditorViewModel(Library, Saver, LedMenu.Core.Logging.NullLog.Instance,
            confirm: (_, _) => ConfirmAnswer,
            askDeleteCategory: (_, _) => CategoryAnswer,
            contentChanged: m => Notified.Add(m),
            inspect: _ => (null, "info"));
    }

    public EditorItemViewModel Item(string name) =>
        Editor.Groups.SelectMany(g => g.Items).First(i => i.Model.Name == name);

    public EditorCategoryViewModel Group(string name) => Editor.Groups.First(g => g.Model?.Name == name);

    public string[] Names(string category) => Group(category).Items.Select(i => i.Model.Name).ToArray();
}

internal static class StoreExtensions
{
    public static void SaveCountReset(this MemoryStore s) => s.ResetCount();
}

public class MenuEditorSelectionTests
{
    [Fact]
    public void The_first_menu_is_selected_and_its_categories_and_items_are_listed_in_order()
    {
        var rig = new EditorRig();
        Assert.Equal(rig.Menu.Id, rig.Editor.SelectedMenuId);
        Assert.True(rig.Editor.HasMenu);
        Assert.Equal(new[] { "Burgers", "Appetizers", "Sides" }, rig.Editor.Groups.Select(g => g.Model!.Name));
        Assert.Equal(new[] { "Classic Burger", "Black & Blue Burger", "Veggie Burger" }, rig.Names("Burgers"));
    }

    [Fact]
    public void With_no_menus_the_editor_is_empty_and_commands_are_disabled()
    {
        var lib = new MenuLibrary(new MemoryStore());
        var editor = new MenuEditorViewModel(lib, new SaveDebouncer(new FakeClock()), LedMenu.Core.Logging.NullLog.Instance,
            (_, _) => true, (_, _) => DeleteCategoryChoice.Cancel, _ => { }, _ => (null, ""));
        Assert.True(editor.HasNoMenu);
        Assert.Empty(editor.Groups);
        Assert.False(editor.AddCategoryCommand.CanExecute(null));
    }

    [Fact]
    public void Choosing_another_menu_saves_pending_typing_first_then_shows_the_other_menu()
    {
        var rig = new EditorRig();
        var second = rig.Library.Create("Second");
        rig.Editor.RefreshMenus();
        rig.Store.ResetCount();
        rig.Item("Fries").Price = "$7.50";                        // typed, still waiting
        Assert.Equal(0, rig.Store.SaveCount);

        rig.Editor.SelectedMenuId = second.Id;

        Assert.Equal("$7.50", rig.Store.SavedCopy(rig.Menu.Id).Items.First(i => i.Name == "Fries").Price);
        Assert.Empty(rig.Editor.Groups);
    }

    [Fact]
    public void Refreshing_after_the_selected_menu_is_deleted_falls_back_to_another_menu()
    {
        var rig = new EditorRig();
        var other = rig.Library.Create("Other");
        rig.Library.Delete(rig.Menu.Id);
        rig.Editor.RefreshMenus();
        Assert.Equal(other.Id, rig.Editor.SelectedMenuId);
    }
}

public class MenuEditorQuickActionTests
{
    [Fact]
    public void Sold_out_takes_effect_and_is_saved_immediately_and_the_item_stays_in_place()
    {
        var rig = new EditorRig();
        var item = rig.Item("Fries");
        var before = rig.Names("Sides");

        item.ToggleSoldOutCommand.Execute(null);

        Assert.True(rig.Menu.Items.First(i => i.Name == "Fries").SoldOut);
        Assert.Equal(1, rig.Store.SaveCount);                                       // no waiting
        Assert.True(rig.Store.SavedCopy(rig.Menu.Id).Items.First(i => i.Name == "Fries").SoldOut);
        Assert.Single(rig.Notified);                                                // the LED / preview were told
        Assert.Equal(before, rig.Names("Sides"));                                   // not moved
        Assert.Contains("Fries", MenuView.Build(rig.Menu).Groups.SelectMany(g => g.Items).Select(i => i.Name));   // still shown
        Assert.Equal("Back in Stock", item.SoldOutButtonText);
        Assert.Contains("SOLD OUT", item.StatusText);

        item.ToggleSoldOutCommand.Execute(null);
        Assert.False(rig.Menu.Items.First(i => i.Name == "Fries").SoldOut);
        Assert.Equal("Sold Out", item.SoldOutButtonText);
        Assert.Equal(2, rig.Store.SaveCount);
    }

    [Fact]
    public void Hide_removes_the_item_from_what_is_shown_at_once_and_show_brings_it_back()
    {
        var rig = new EditorRig();
        var item = rig.Item("Onion Rings");

        item.ToggleVisibleCommand.Execute(null);
        Assert.DoesNotContain("Onion Rings", MenuView.Build(rig.Menu).Groups.SelectMany(g => g.Items).Select(i => i.Name));
        Assert.Equal(1, rig.Store.SaveCount);
        Assert.Equal("Show", item.VisibilityButtonText);
        Assert.True(item.RowOpacity < 1);
        Assert.Equal(new[] { "Fries", "Onion Rings", "Coleslaw", "Daily Special" }, rig.Names("Sides"));   // still in the list to unhide

        item.ToggleVisibleCommand.Execute(null);
        Assert.Contains("Onion Rings", MenuView.Build(rig.Menu).Groups.SelectMany(g => g.Items).Select(i => i.Name));
        Assert.Equal("Hide", item.VisibilityButtonText);
        Assert.Equal(1.0, item.RowOpacity);
    }

    [Fact]
    public void Sold_out_and_hide_are_independent()
    {
        var rig = new EditorRig();
        var item = rig.Item("Coleslaw");
        item.ToggleSoldOutCommand.Execute(null);
        item.ToggleVisibleCommand.Execute(null);
        Assert.True(item.Model.SoldOut && !item.Model.Visible);
        item.ToggleVisibleCommand.Execute(null);
        Assert.True(item.Model.SoldOut && item.Model.Visible);
    }

    [Fact]
    public void Featured_is_saved_immediately()
    {
        var rig = new EditorRig();
        var item = rig.Item("Fries");
        item.Featured = true;
        Assert.True(rig.Store.SavedCopy(rig.Menu.Id).Items.First(i => i.Name == "Fries").Featured);
        Assert.Equal(1, rig.Store.SaveCount);
    }

    [Fact]
    public void Hiding_a_category_hides_all_its_items_without_touching_them()
    {
        var rig = new EditorRig();
        var group = rig.Group("Appetizers");
        group.ToggleVisibleCommand.Execute(null);
        Assert.DoesNotContain(MenuView.Build(rig.Menu).Groups, g => g.Category?.Name == "Appetizers");
        Assert.All(rig.Menu.Items.Where(i => i.CategoryId == group.Model!.Id), i => Assert.True(i.Visible));
        Assert.Equal("Show Category", group.VisibilityButtonText);
        Assert.Equal(1, rig.Store.SaveCount);
    }

    [Fact]
    public void The_list_is_not_rebuilt_by_a_toggle_so_typing_in_other_boxes_is_not_disturbed()
    {
        var rig = new EditorRig();
        var item = rig.Item("Fries");
        var itemsBefore = rig.Editor.Groups.SelectMany(g => g.Items).ToList();
        item.ToggleSoldOutCommand.Execute(null);
        Assert.Equal(itemsBefore, rig.Editor.Groups.SelectMany(g => g.Items));
    }
}

public class MenuEditorTypingTests
{
    [Fact]
    public void Typing_a_price_updates_the_menu_at_once_but_saves_once_after_a_pause()
    {
        var rig = new EditorRig();
        var item = rig.Item("Fries");

        foreach (var text in new[] { "$", "$1", "$12", "$12.", "$12.5", "$12.50" })
        {
            item.Price = text;
            rig.Clock.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal("$12.50", rig.Menu.Items.First(i => i.Name == "Fries").Price);     // the model has it already
        Assert.Equal(0, rig.Store.SaveCount);                                           // nothing written per keystroke
        Assert.Empty(rig.Notified);

        rig.Clock.Advance(TimeSpan.FromMilliseconds(500));

        Assert.Equal(1, rig.Store.SaveCount);
        Assert.Equal("$12.50", rig.Store.SavedCopy(rig.Menu.Id).Items.First(i => i.Name == "Fries").Price);
        Assert.Single(rig.Notified);
    }

    [Fact]
    public void Flush_saves_typing_straight_away_for_when_focus_leaves_the_box()
    {
        var rig = new EditorRig();
        rig.Item("Fries").Price = "$9";
        rig.Editor.FlushPending();
        Assert.Equal(1, rig.Store.SaveCount);
        Assert.Equal("$9", rig.Store.SavedCopy(rig.Menu.Id).Items.First(i => i.Name == "Fries").Price);
        rig.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, rig.Store.SaveCount);                                           // the timer did not save a second time
    }

    [Fact]
    public void A_quick_action_while_typing_saves_the_typed_text_with_it_in_one_write()
    {
        var rig = new EditorRig();
        rig.Item("Fries").Price = "$8";
        rig.Item("Coleslaw").ToggleSoldOutCommand.Execute(null);
        Assert.Equal(1, rig.Store.SaveCount);
        var saved = rig.Store.SavedCopy(rig.Menu.Id);
        Assert.Equal("$8", saved.Items.First(i => i.Name == "Fries").Price);
        Assert.True(saved.Items.First(i => i.Name == "Coleslaw").SoldOut);
        rig.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, rig.Store.SaveCount);
    }

    [Fact]
    public void Exit_flush_loses_nothing_that_was_typed()
    {
        var rig = new EditorRig();
        rig.Item("Fries").Name = "Curly Fries";
        rig.Item("Coleslaw").Description = "Creamy";
        rig.Saver.FlushAll();                       // what the program does when it exits
        var saved = rig.Store.SavedCopy(rig.Menu.Id);
        Assert.Contains(saved.Items, i => i.Name == "Curly Fries");
        Assert.Equal("Creamy", saved.Items.First(i => i.Name == "Coleslaw").Description);
    }

    [Fact]
    public void Editing_a_name_or_description_or_category_name_follows_the_same_rules()
    {
        var rig = new EditorRig();
        rig.Item("Fries").Name = "Chips";
        rig.Item("Chips").Description = "Hot";
        rig.Group("Sides").Name = "Extras";
        Assert.Equal(0, rig.Store.SaveCount);
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, rig.Store.SaveCount);
        var saved = rig.Store.SavedCopy(rig.Menu.Id);
        Assert.Contains(saved.Categories, c => c.Name == "Extras");
        Assert.Equal("Hot", saved.Items.First(i => i.Name == "Chips").Description);
    }

    [Fact]
    public void Prices_are_free_text_and_saved_exactly()
    {
        var rig = new EditorRig();
        foreach (var price in new[] { "$12 / $16", "Market Price", "3 for $10", "" })
        {
            rig.Item("Fries").Price = price;
            rig.Editor.FlushPending();
            Assert.Equal(price, rig.Store.SavedCopy(rig.Menu.Id).Items.First(i => i.Name == "Fries").Price);
        }
    }

    [Fact]
    public void A_save_failure_is_reported_and_the_edit_stays_on_screen()
    {
        var rig = new EditorRig();
        rig.Store.FailSaves = true;
        var item = rig.Item("Fries");
        item.ToggleSoldOutCommand.Execute(null);                    // must not throw
        Assert.True(rig.Editor.HasMessage);
        Assert.Contains("disk full", rig.Editor.Message);
        Assert.True(item.Model.SoldOut);                            // the change is still there
        rig.Store.FailSaves = false;
        item.ToggleSoldOutCommand.Execute(null);
        item.ToggleSoldOutCommand.Execute(null);
        Assert.False(rig.Editor.HasMessage);                        // cleared by the next good save
        Assert.True(rig.Store.SavedCopy(rig.Menu.Id).Items.First(i => i.Name == "Fries").SoldOut);
    }

    [Fact]
    public void A_failed_save_is_retried_by_itself_once_the_problem_clears()
    {
        var rig = new EditorRig();
        rig.Store.FailSaves = true;
        rig.Item("Fries").ToggleSoldOutCommand.Execute(null);
        Assert.True(rig.Editor.HasMessage);

        rig.Clock.Advance(TimeSpan.FromSeconds(6));                 // still failing: tries again, still reports it
        Assert.True(rig.Editor.HasMessage);
        rig.Store.FailSaves = false;                                // the disk has room again / the lock is gone
        rig.Clock.Advance(TimeSpan.FromSeconds(6));

        Assert.False(rig.Editor.HasMessage);
        Assert.True(rig.Store.SavedCopy(rig.Menu.Id).Items.First(i => i.Name == "Fries").SoldOut);
    }

    [Fact]
    public void Closing_the_program_makes_one_last_attempt_at_a_failed_save()
    {
        var rig = new EditorRig();
        rig.Store.FailSaves = true;
        rig.Item("Fries").ToggleSoldOutCommand.Execute(null);
        rig.Store.FailSaves = false;
        rig.Editor.FlushPending();                                  // what exit does
        Assert.True(rig.Store.SavedCopy(rig.Menu.Id).Items.First(i => i.Name == "Fries").SoldOut);
    }

    [Fact]
    public void A_save_that_keeps_failing_does_not_hammer_the_disk_or_the_log()
    {
        var rig = new EditorRig();
        rig.Store.FailSaves = true;
        rig.Item("Fries").ToggleSoldOutCommand.Execute(null);
        var before = rig.Store.FailedAttempts;
        rig.Clock.Advance(TimeSpan.FromSeconds(4));                 // less than the retry delay: no further attempt
        Assert.Equal(before, rig.Store.FailedAttempts);
        rig.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(before + 1, rig.Store.FailedAttempts);
    }
}

public class MenuEditorStructureTests
{
    [Fact]
    public void Add_item_appends_to_that_category_opens_for_editing_and_is_saved()
    {
        var rig = new EditorRig();
        rig.Group("Sides").AddItemCommand.Execute(null);
        var items = rig.Group("Sides").Items;
        Assert.Equal("New item", items[^1].Model.Name);
        Assert.True(items[^1].IsEditing);
        Assert.Equal(5, items.Count);
        Assert.Equal(1, rig.Store.SaveCount);
        Assert.Contains(rig.Store.SavedCopy(rig.Menu.Id).Items, i => i.Name == "New item");
    }

    [Fact]
    public void An_item_added_with_no_categories_goes_in_the_no_category_section()
    {
        var rig = new EditorRig(new Menu { Name = "Blank", HeaderText = "BLANK" });
        rig.Editor.AddLooseItemCommand.Execute(null);
        var group = Assert.Single(rig.Editor.Groups);
        Assert.True(group.IsUncategorized);
        Assert.Equal("New item", Assert.Single(group.Items).Model.Name);
    }

    [Fact]
    public void Add_category_appends_a_new_section()
    {
        var rig = new EditorRig();
        rig.Editor.AddCategoryCommand.Execute(null);
        Assert.Equal("New category", rig.Editor.Groups[^1].Model!.Name);
        Assert.Equal(4, rig.Editor.Groups.Count);
        Assert.Equal(1, rig.Store.SaveCount);
    }

    [Fact]
    public void Delete_item_asks_first_and_does_nothing_on_no()
    {
        var rig = new EditorRig();
        rig.ConfirmAnswer = false;
        rig.Item("Fries").DeleteCommand.Execute(null);
        Assert.Contains(rig.Menu.Items, i => i.Name == "Fries");
        Assert.Equal(0, rig.Store.SaveCount);

        rig.ConfirmAnswer = true;
        rig.Item("Fries").DeleteCommand.Execute(null);
        Assert.DoesNotContain(rig.Menu.Items, i => i.Name == "Fries");
        Assert.DoesNotContain("Fries", rig.Names("Sides"));
        Assert.Equal(1, rig.Store.SaveCount);
    }

    [Fact]
    public void Deleting_a_category_can_keep_its_items_as_uncategorized()
    {
        var rig = new EditorRig();
        rig.CategoryAnswer = DeleteCategoryChoice.KeepItems;
        rig.Group("Sides").DeleteCommand.Execute(null);
        Assert.DoesNotContain(rig.Editor.Groups, g => g.Model?.Name == "Sides");
        var loose = rig.Editor.Groups.Single(g => g.IsUncategorized);
        Assert.Equal(4, loose.Items.Count);
        Assert.Contains(rig.Menu.Items, i => i.Name == "Fries");
    }

    [Fact]
    public void Deleting_a_category_can_delete_its_items_too()
    {
        var rig = new EditorRig();
        rig.CategoryAnswer = DeleteCategoryChoice.DeleteItemsToo;
        rig.Group("Sides").DeleteCommand.Execute(null);
        Assert.DoesNotContain(rig.Menu.Items, i => i.Name == "Fries");
        Assert.Equal(6, rig.Menu.Items.Count);
    }

    [Fact]
    public void Cancelling_the_category_question_changes_nothing()
    {
        var rig = new EditorRig();
        rig.CategoryAnswer = DeleteCategoryChoice.Cancel;
        rig.Group("Sides").DeleteCommand.Execute(null);
        Assert.Equal(3, rig.Menu.Categories.Count);
        Assert.Equal(0, rig.Store.SaveCount);
    }

    [Fact]
    public void An_empty_category_only_needs_a_yes_no_confirmation()
    {
        var rig = new EditorRig();
        rig.Editor.AddCategoryCommand.Execute(null);
        rig.CategoryAnswer = DeleteCategoryChoice.Cancel;            // the three-way question must not be asked
        rig.ConfirmAnswer = true;
        rig.Editor.Groups[^1].DeleteCommand.Execute(null);
        Assert.Equal(3, rig.Editor.Groups.Count);
    }

    [Fact]
    public void Items_move_up_and_down_within_their_category_and_the_order_is_saved()
    {
        var rig = new EditorRig();
        rig.Item("Veggie Burger").MoveUpCommand.Execute(null);
        Assert.Equal(new[] { "Classic Burger", "Veggie Burger", "Black & Blue Burger" }, rig.Names("Burgers"));
        rig.Item("Classic Burger").MoveDownCommand.Execute(null);
        Assert.Equal(new[] { "Veggie Burger", "Classic Burger", "Black & Blue Burger" }, rig.Names("Burgers"));
        // moving the first item up or the last down is harmless
        rig.Item("Veggie Burger").MoveUpCommand.Execute(null);
        rig.Item("Black & Blue Burger").MoveDownCommand.Execute(null);
        Assert.Equal(new[] { "Veggie Burger", "Classic Burger", "Black & Blue Burger" }, rig.Names("Burgers"));

        var saved = rig.Store.SavedCopy(rig.Menu.Id);
        Assert.Equal(new[] { "Veggie Burger", "Classic Burger", "Black & Blue Burger" },
            MenuOrdering.Items(saved, saved.Categories.First(c => c.Name == "Burgers").Id).Select(i => i.Name));
    }

    [Fact]
    public void Categories_move_up_and_down()
    {
        var rig = new EditorRig();
        rig.Group("Sides").MoveUpCommand.Execute(null);
        Assert.Equal(new[] { "Burgers", "Sides", "Appetizers" }, rig.Editor.Groups.Select(g => g.Model!.Name));
        rig.Group("Burgers").MoveDownCommand.Execute(null);
        Assert.Equal(new[] { "Sides", "Burgers", "Appetizers" }, rig.Editor.Groups.Select(g => g.Model!.Name));
        Assert.Equal(new[] { "Sides", "Burgers", "Appetizers" },
            MenuOrdering.Categories(rig.Store.SavedCopy(rig.Menu.Id)).Select(c => c.Name));
    }

    [Fact]
    public void An_item_can_be_moved_to_another_category_or_to_none()
    {
        var rig = new EditorRig();
        var sides = rig.Group("Sides").Model!.Id;
        rig.Item("Veggie Burger").CategoryKey = sides;
        Assert.DoesNotContain("Veggie Burger", rig.Names("Burgers"));
        Assert.Equal("Veggie Burger", rig.Names("Sides")[^1]);

        rig.Item("Veggie Burger").CategoryKey = Guid.Empty;
        Assert.DoesNotContain("Veggie Burger", rig.Names("Sides"));
        Assert.Contains("Veggie Burger", rig.Editor.Groups.Single(g => g.IsUncategorized).Items.Select(i => i.Model.Name));
    }

    [Fact]
    public void An_item_being_edited_stays_open_when_the_list_is_rebuilt_by_another_action()
    {
        var rig = new EditorRig();
        rig.Item("Fries").IsEditing = true;
        rig.Item("Coleslaw").MoveUpCommand.Execute(null);            // rebuilds the lists
        Assert.True(rig.Item("Fries").IsEditing);
        Assert.False(rig.Item("Onion Rings").IsEditing);
    }

    [Fact]
    public void Every_structural_change_tells_the_output_exactly_once()
    {
        var rig = new EditorRig();
        rig.Editor.AddCategoryCommand.Execute(null);
        Assert.Single(rig.Notified);
        rig.Group("Sides").AddItemCommand.Execute(null);
        Assert.Equal(2, rig.Notified.Count);
        Assert.All(rig.Notified, m => Assert.Same(rig.Menu, m));
    }
}

public class MenuEditorOutputEffectTests
{
    [Fact]
    public void Hiding_items_in_the_editor_reduces_the_pages_the_renderer_lays_out()
    {
        var rig = new EditorRig(SampleMenus.DenseMenu());
        var before = LedMenu.Core.Layout.MenuLayoutEngine.Layout(MenuView.Build(rig.Menu), rig.Menu.Theme, 336, 672, null, new FixedMeasurer()).PageCount;
        foreach (var name in rig.Menu.Items.Select(i => i.Name).ToList().Skip(2))
            rig.Item(name).ToggleVisibleCommand.Execute(null);
        var after = LedMenu.Core.Layout.MenuLayoutEngine.Layout(MenuView.Build(rig.Menu), rig.Menu.Theme, 336, 672, null, new FixedMeasurer()).PageCount;
        Assert.True(before > 1);
        Assert.Equal(1, after);
    }

    private sealed class FixedMeasurer : LedMenu.Core.Layout.ITextMeasurer
    {
        public double LineHeight(LedMenu.Core.Layout.TextStyle style) => style.Size * 1.25;
        public double Width(string text, LedMenu.Core.Layout.TextStyle style) => text.Length * style.Size * 0.5;
    }
}
