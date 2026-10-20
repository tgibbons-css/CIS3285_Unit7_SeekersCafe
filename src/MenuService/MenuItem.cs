namespace MenuService;

/// <summary>
/// Represents a cafe menu item stored by MenuService, including its name, description,
/// category, current price, and availability for ordering.
/// </summary>
public sealed class MenuItem
{
    public int MenuItemId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsAvailable { get; set; }
}
