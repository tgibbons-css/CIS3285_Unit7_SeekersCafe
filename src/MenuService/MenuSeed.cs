namespace MenuService;

public static class MenuSeed
{
    // Illustrative products and prices, not a verified campus menu.
    public static MenuItem[] CreateItems() =>
    [
        new() { Name = "Latte", Description = "Espresso with steamed milk.", Category = "Coffee", Price = 4.50m, IsAvailable = true },
        new() { Name = "Cappuccino", Description = "Espresso, steamed milk, and foam.", Category = "Coffee", Price = 4.25m, IsAvailable = true },
        new() { Name = "Drip Coffee", Description = "Freshly brewed coffee.", Category = "Coffee", Price = 2.50m, IsAvailable = true },
        new() { Name = "Hot Chocolate", Description = "Warm cocoa with steamed milk.", Category = "Other Drinks", Price = 3.50m, IsAvailable = true },
        new() { Name = "Blueberry Muffin", Description = "A soft muffin with blueberries.", Category = "Bakery", Price = 3.00m, IsAvailable = true },
        new() { Name = "Breakfast Sandwich", Description = "Egg and cheese on an English muffin.", Category = "Breakfast", Price = 5.75m, IsAvailable = true },
        new() { Name = "Bagel", Description = "Toasted bagel with cream cheese.", Category = "Bakery", Price = 2.75m, IsAvailable = true }
    ];
}
