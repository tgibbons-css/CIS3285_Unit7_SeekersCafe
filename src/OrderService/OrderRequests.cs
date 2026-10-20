namespace OrderService;

/// <summary>
/// Carries and validates the customer details, item selections, and payment method supplied at checkout.
/// Prices, totals, timestamps, and initial status are determined by the server.
/// </summary>
public sealed record CreateOrderRequest(string? CustomerName, List<OrderItemRequest?>? Items, string? PaymentMethod)
{
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (PaymentMethod is not ("Credit Card" or "Dining Dollars"))
            errors["paymentMethod"] = ["Choose Credit Card or Dining Dollars (simulated payments only)."];
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

/// <summary>
/// Identifies a menu item and the quantity the customer wants to order.
/// </summary>
public sealed record OrderItemRequest(int MenuItemId, int Quantity);

/// <summary>
/// Carries a requested order status change for validation against the allowed workflow transitions.
/// </summary>
public sealed record UpdateStatusRequest(string? Status);
