using Microsoft.EntityFrameworkCore;

namespace OrderService;

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this WebApplication app)
    {
        var orders = app.MapGroup("/api/orders");
        orders.MapGet("", async (OrderDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Orders.AsNoTracking().Include(x => x.OrderItems)
                .OrderByDescending(x => x.OrderDate).ThenByDescending(x => x.OrderId).ToListAsync(ct)));

        orders.MapGet("/{id:int}", async (int id, OrderDbContext db, CancellationToken ct) =>
        {
            var order = await db.Orders.AsNoTracking().Include(x => x.OrderItems)
                .SingleOrDefaultAsync(x => x.OrderId == id, ct);
            return order is null ? Results.NotFound() : Results.Ok(order);
        });

        orders.MapPost("", async (CreateOrderRequest request, OrderDbContext db, MenuClient menu, CancellationToken ct) =>
        {
            var errors = request.Validate();
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            Dictionary<int, MenuProduct?> products;
            try
            {
                products = await menu.GetItemsAsync(request.Items!.Select(x => x!.MenuItemId), ct);
            }
            catch (MenuUnavailableException ex)
            {
                app.Logger.LogWarning(ex, "Order rejected because menu validation failed");
                return Results.Problem(statusCode: 503, title: "MenuService unavailable", detail: ex.Message);
            }

            var order = new Order { CustomerName = request.CustomerName!.Trim(), OrderDate = DateTime.UtcNow };
            for (var index = 0; index < request.Items!.Count; index++)
            {
                var line = request.Items[index]!;
                var product = products[line.MenuItemId];
                if (product is null || !product.IsAvailable)
                {
                    errors[$"items[{index}].menuItemId"] = [product is null
                        ? $"Menu item {line.MenuItemId} does not exist."
                        : $"Menu item {line.MenuItemId} is unavailable."];
                    continue;
                }
                // Snapshot names and prices: later menu edits cannot rewrite order history.
                order.OrderItems.Add(new OrderItem
                {
                    MenuItemId = product.MenuItemId, ItemName = product.Name,
                    Quantity = line.Quantity, UnitPrice = product.Price
                });
                order.TotalAmount += product.Price * line.Quantity;
            }
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            // Future InventoryService validation belongs before saving this Pending order.
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct); // Order and its lines commit in one local transaction.
            app.Logger.LogInformation("Created order {OrderId} with {ItemCount} lines and total {TotalAmount}; status {Status}",
                order.OrderId, order.OrderItems.Count, order.TotalAmount, order.Status);
            // Phase 4 will call PaymentService here, after Pending is persisted.
            return Results.Created($"/api/orders/{order.OrderId}", order);
        });

        orders.MapPut("/{id:int}/status", async (int id, UpdateStatusRequest request, OrderDbContext db, CancellationToken ct) =>
        {
            // Enum.TryParse alone accepts numeric strings, so require an explicit status name.
            if (request.Status is null || !Enum.GetNames<OrderStatus>().Contains(request.Status))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                { ["status"] = ["Use Pending, Paid, Preparing, Ready, Completed, or Cancelled (case sensitive)."] });
            var status = Enum.Parse<OrderStatus>(request.Status);
            var order = await db.Orders.AsNoTracking().Include(x => x.OrderItems)
                .SingleOrDefaultAsync(x => x.OrderId == id, ct);
            if (order is null) return Results.NotFound();
            if (status == order.Status) return Results.Ok(order);
            if (order.Status != OrderStatus.Pending || status != OrderStatus.Cancelled)
                return Results.Problem(statusCode: 409, title: "Status transition not allowed",
                    detail: "Phase 3 allows only Pending to Cancelled. Payment and preparation transitions become available after payment integration.");

            // Compare-and-update prevents racing requests from overwriting a newer status.
            var changed = await db.Orders.Where(x => x.OrderId == id && x.Status == OrderStatus.Pending)
                .ExecuteUpdateAsync(updates => updates.SetProperty(x => x.Status, OrderStatus.Cancelled), ct);
            if (changed == 0)
                return Results.Problem(statusCode: 409, title: "Order status changed; retrieve the order and retry.");
            order.Status = OrderStatus.Cancelled;
            app.Logger.LogInformation("Order {OrderId} status changed from Pending to {Status}", id, order.Status);
            return Results.Ok(order);
        });
    }
}
