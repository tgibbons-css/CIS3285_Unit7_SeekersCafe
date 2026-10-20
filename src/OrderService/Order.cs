namespace OrderService;

/// <summary>
/// Defines the stages an order can occupy during payment, preparation, fulfillment, or cancellation.
/// </summary>
public enum OrderStatus { Pending, Paid, Preparing, Ready, Completed, Cancelled }

/// <summary>
/// Stores a customer's order, including its creation time, workflow status, server-calculated total, and line items.
/// </summary>
public sealed class Order
{
    public int OrderId { get; set; }
    public string CustomerName { get; set; } = "";
    public DateTime OrderDate { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal TotalAmount { get; set; }
    public List<OrderItem> OrderItems { get; set; } = [];
}

/// <summary>
/// Stores an ordered menu item's quantity and a snapshot of its name and price to preserve the purchase history.
/// </summary>
public sealed class OrderItem
{
    public int OrderItemId { get; set; }
    public int OrderId { get; set; }
    // This is an external service's ID, not a foreign key into its database.
    public int MenuItemId { get; set; }
    public string ItemName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
