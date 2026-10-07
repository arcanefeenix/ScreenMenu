namespace LedMenu.Core.Menus;

/// <summary>Example content so a template can be reviewed before the editor exists. Not used automatically.</summary>
public static class SampleMenus
{
    public static Menu FestivalFood()
    {
        var menu = new Menu
        {
            Name = "Festival Food",
            HeaderText = "FESTIVAL FOOD",
            Subtitle = "Fresh off the grill",
        };

        var burgers = MenuEditor.AddCategory(menu, "Burgers");
        var apps = MenuEditor.AddCategory(menu, "Appetizers");
        var sides = MenuEditor.AddCategory(menu, "Sides");

        MenuEditor.AddItem(menu, burgers.Id, "Classic Burger", "$12", "Lettuce • Tomato • Onion");
        var bb = MenuEditor.AddItem(menu, burgers.Id, "Black & Blue Burger", "$14", "Bacon • Blue Cheese • Onion");
        MenuEditor.SetFeatured(menu, bb.Id, true);
        MenuEditor.AddItem(menu, burgers.Id, "Veggie Burger", "$11", "House-made patty");
        MenuEditor.AddItem(menu, apps.Id, "Loaded Nachos", "$9", "Cheese • Jalapeños • Salsa");
        MenuEditor.AddItem(menu, apps.Id, "Soft Pretzel", "$6 / $9", "Single or double");
        MenuEditor.AddItem(menu, apps.Id, "Wings", "3 for $10", "Buffalo or BBQ");
        MenuEditor.AddItem(menu, sides.Id, "Fries", "$6");
        MenuEditor.AddItem(menu, sides.Id, "Onion Rings", "$7");
        MenuEditor.AddItem(menu, sides.Id, "Coleslaw", "$4");
        MenuEditor.AddItem(menu, sides.Id, "Daily Special", "Market Price", "Ask at the window");
        return menu;
    }
}
