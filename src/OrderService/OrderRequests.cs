namespace OrderService;

// Client prices, totals, timestamps and statuses are intentionally absent.
public sealed record CreateOrderRequest(string? CustomerName, List<OrderItemRequest?>? Items)
{
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(CustomerName) || CustomerName.Trim().Length > 100)
            errors["customerName"] = ["Customer name is required and must be at most 100 characters."];
        if (Items is null || Items.Count is < 1 or > 50)
        {
            errors["items"] = ["Supply between 1 and 50 distinct menu items."];
            return errors;
        }
        var seen = new HashSet<int>();
        for (var index = 0; index < Items.Count; index++)
        {
            var item = Items[index];
            if (item is null || item.MenuItemId <= 0 || item.Quantity is < 1 or > 20)
                errors[$"items[{index}]"] = ["Supply a positive menuItemId and a quantity from 1 to 20."];
            else if (!seen.Add(item.MenuItemId))
                errors[$"items[{index}].menuItemId"] = ["Combine quantities into one line per menu item."];
        }
        return errors;
    }
}

public sealed record OrderItemRequest(int MenuItemId, int Quantity);
public sealed record UpdateStatusRequest(string? Status);
