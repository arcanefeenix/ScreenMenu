namespace LedMenu.Core.Menus;

public sealed record SampleMenuInfo(string Key, string Title, string Purpose, Func<Menu> Create);

/// <summary>
/// Example content so templates can be reviewed on the real LED wall before the editor exists.
/// None of these is used automatically, and none contains any real branding.
/// </summary>
public static class SampleMenus
{
    public static IReadOnlyList<SampleMenuInfo> All { get; } = new[]
    {
        new SampleMenuInfo("food", "Festival Food", "A realistic everyday menu that fits one page.", FestivalFood),
        new SampleMenuInfo("dense", "Dense Menu", "Many items, to show pagination.", DenseMenu),
        new SampleMenuInfo("soldout", "Sold Out Demo", "A normal menu with several items sold out and one featured.", SoldOutDemo),
        new SampleMenuInfo("stress", "Long Text Stress", "Very long names, descriptions and prices.", LongTextStress),
    };

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

    /// <summary>Roughly three screens' worth of content so pagination, continued categories and the page number are all visible.</summary>
    public static Menu DenseMenu()
    {
        var menu = new Menu
        {
            Name = "Dense Menu",
            HeaderText = "BIG MENU",
            Subtitle = "Everything we make",
        };
        var mains = MenuEditor.AddCategory(menu, "Mains");
        var sides = MenuEditor.AddCategory(menu, "Sides");
        var desserts = MenuEditor.AddCategory(menu, "Desserts");
        var drinks = MenuEditor.AddCategory(menu, "Drinks");

        void Add(Guid cat, string name, string price, string desc = "") => MenuEditor.AddItem(menu, cat, name, price, desc);

        Add(mains.Id, "Smash Burger", "$13", "Double patty • American cheese • Pickles");
        Add(mains.Id, "Chicken Sandwich", "$12", "Crispy thigh • Slaw • Hot honey");
        Add(mains.Id, "Pulled Pork Plate", "$15", "Slow smoked • Two sides");
        Add(mains.Id, "Brisket Sandwich", "$16", "Smoked 14 hours • Pickled onion");
        Add(mains.Id, "Veggie Wrap", "$10", "Hummus • Greens • Roasted peppers");
        Add(mains.Id, "Fish Tacos", "3 for $12", "Cod • Cabbage • Lime crema");
        Add(mains.Id, "Sausage & Peppers", "$11", "Grilled • Hoagie roll");
        Add(mains.Id, "Mac & Cheese Bowl", "$9 / $12", "Small or large");
        Add(sides.Id, "Fries", "$6", "Salted or seasoned");
        Add(sides.Id, "Sweet Potato Fries", "$7");
        Add(sides.Id, "Onion Rings", "$7", "Beer battered");
        Add(sides.Id, "Corn on the Cob", "$5", "Butter • Cotija");
        Add(sides.Id, "Coleslaw", "$4");
        Add(sides.Id, "Baked Beans", "$4");
        Add(desserts.Id, "Funnel Cake", "$8", "Powdered sugar");
        Add(desserts.Id, "Churros", "$6", "Cinnamon sugar • Chocolate dip");
        Add(desserts.Id, "Ice Cream Sandwich", "$5");
        Add(desserts.Id, "Fried Oreos", "$7", "Six pieces");
        Add(drinks.Id, "Lemonade", "$4 / $6", "Fresh squeezed");
        Add(drinks.Id, "Iced Tea", "$3");
        Add(drinks.Id, "Craft Soda", "$4");
        Add(drinks.Id, "Bottled Water", "$2");
        return menu;
    }

    /// <summary>The festival menu with several items marked sold out and one featured, to review those treatments.</summary>
    public static Menu SoldOutDemo()
    {
        var menu = FestivalFood();
        menu.Name = "Sold Out Demo";
        menu.HeaderText = "FESTIVAL FOOD";
        foreach (var name in new[] { "Veggie Burger", "Soft Pretzel", "Onion Rings" })
            MenuEditor.SetSoldOut(menu, menu.Items.First(i => i.Name == name).Id, true);
        MenuEditor.SetFeatured(menu, menu.Items.First(i => i.Name == "Wings").Id, true);
        return menu;
    }

    public static Menu LongTextStress()
    {
        var menu = new Menu
        {
            Name = "Long Text Stress",
            HeaderText = "THE GREAT ANNUAL FESTIVAL OF FOOD AND DRINK",
            Subtitle = "A deliberately long subtitle that has to wrap onto more than one line",
        };
        var c = MenuEditor.AddCategory(menu, "Specials and Limited Time Offers");
        MenuEditor.AddItem(menu, c.Id, "Smoked Brisket and Pulled Pork Combination Platter", "$22",
            "Two meats, two sides, a roll, and a pickle spear. Add a third meat for a little extra.");
        MenuEditor.AddItem(menu, c.Id, "Supercalifragilisticexpialidocious Sundae", "Market Price (ask us)", "Sweet.");
        MenuEditor.AddItem(menu, c.Id, "Tiny", "$1");
        var sold = MenuEditor.AddItem(menu, c.Id, "Seasonal Berry and Goat Cheese Flatbread", "$13 / $17 / $21", "Honey drizzle");
        MenuEditor.SetSoldOut(menu, sold.Id, true);
        MenuEditor.AddItem(menu, c.Id, "Jalapeño Poppers — \"Extra Hot\"", "6 for $9");
        return menu;
    }
}
