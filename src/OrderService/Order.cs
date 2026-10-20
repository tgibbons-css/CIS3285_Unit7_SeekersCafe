namespace OrderService;

public enum OrderStatus { Pending, Paid, Preparing, Ready, Completed, Cancelled }

public sealed class Order
{
    public int OrderId { get; set; }
    public string CustomerName { get; set; } = "";
    public DateTime OrderDate { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal TotalAmount { get; set; }
    public List<OrderItem> OrderItems { get; set; } = [];
}

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
