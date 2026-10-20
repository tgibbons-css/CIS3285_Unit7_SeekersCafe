namespace MenuService;

/// <summary>
/// Carries, validates, and applies client input when creating or updating a menu item without
/// allowing clients to assign database IDs. Nullable values distinguish omitted fields from zero and false.
/// </summary>
public sealed record MenuItemRequest(
    string? Name, string? Description, string? Category, decimal? Price, bool? IsAvailable)
{
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 100)
            errors["name"] = ["Name is required and must be at most 100 characters."];
        if (Description is null || Description.Length > 500)
            errors["description"] = ["Description is required and must be at most 500 characters (empty is allowed)."];
        if (string.IsNullOrWhiteSpace(Category) || Category.Trim().Length > 50)
            errors["category"] = ["Category is required and must be at most 50 characters."];
        if (Price is null || Price < 0 || Price > 1000 || decimal.Round(Price.Value, 2) != Price.Value)
            errors["price"] = ["Price is required, from 0 to 1000 dollars, with at most two decimal places."];
        if (IsAvailable is null)
            errors["isAvailable"] = ["IsAvailable is required and must be true or false."];
        return errors;
    }

    public void ApplyTo(MenuItem item)
    {
        item.Name = Name!.Trim();
        item.Description = Description!.Trim();
        item.Category = Category!.Trim();
        item.Price = Price!.Value;
        item.IsAvailable = IsAvailable!.Value;
    }
}
