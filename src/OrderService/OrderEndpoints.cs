using Microsoft.EntityFrameworkCore;

namespace OrderService;

/// <summary>
/// Maps the HTTP endpoints for retrieving orders, validating checkout, reconciling payments,
/// and enforcing allowed order status transitions.
/// </summary>
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

        // POST /api/orders: Validates checkout against the current menu, saves a Pending order
        // with price snapshots and a server-calculated total, then requests a simulated payment.
        // Confirmed success marks the order Paid; an uncertain outcome requires reconciliation.
        orders.MapPost("", async (CreateOrderRequest request, OrderDbContext db, MenuClient menu, PaymentClient payments, CancellationToken ct) =>
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
            PaymentRecord payment;
            try { payment = await payments.PayAsync(order, request.PaymentMethod!, ct); }
            catch (PaymentUnknownException ex)
            {
                app.Logger.LogWarning(ex, "Payment outcome unknown for order {OrderId}, request {RequestId}",
                    order.OrderId, PaymentClient.RequestId(order));
                return PaymentWorkflow.Unknown(order, ex.Message);
            }
            await PaymentWorkflow.ApplyConfirmedAsync(order, payment, db, ct);
            app.Logger.LogInformation("Order {OrderId} payment {PaymentId} confirmed {PaymentStatus}; order status {Status}",
                order.OrderId, payment.PaymentId, payment.Status, order.Status);
            if (payment.Status == "Declined")
                return Results.Problem(statusCode: 402, title: "Simulated payment declined",
                    detail: "The order was saved as Pending. No successful payment occurred. This demo does not retry declined payments.",
                    extensions: new Dictionary<string, object?>
                    { ["orderId"] = order.OrderId, ["order"] = order, ["paymentStatus"] = payment.Status, ["paymentId"] = payment.PaymentId });
            return Results.Created($"/api/orders/{order.OrderId}", new { order, payment });
        });

        // POST /api/orders/{id}/payment/reconcile: Retrieves the order's existing payment result
        // and marks a Pending order Paid when success is confirmed, without initiating another payment.
        orders.MapPost("/{id:int}/payment/reconcile", async (int id, OrderDbContext db, PaymentClient payments, CancellationToken ct) =>
        {
            var order = await db.Orders.AsNoTracking().Include(x => x.OrderItems).SingleOrDefaultAsync(x => x.OrderId == id, ct);
            if (order is null) return Results.NotFound();
            PaymentRecord? payment;
            try { payment = await payments.GetAsync(order, ct); }
            catch (PaymentUnknownException ex) { return PaymentWorkflow.Unknown(order, ex.Message); }
            if (payment is null)
                return Results.Problem(statusCode: 409, title: "No payment record found",
                    detail: "No confirmed payment is available yet. Reconciliation never initiates payment. The order was not changed.",
                    extensions: new Dictionary<string, object?> { ["orderId"] = id, ["paymentStatus"] = "Unknown" });
            await PaymentWorkflow.ApplyConfirmedAsync(order, payment, db, ct);
            app.Logger.LogInformation("Reconciled order {OrderId} with payment {PaymentId}, outcome {PaymentStatus}; status {Status}",
                id, payment.PaymentId, payment.Status, order.Status);
            return Results.Ok(new { order, payment });
        });

        orders.MapPut("/{id:int}/status", async (int id, UpdateStatusRequest request, OrderDbContext db, PaymentClient payments, CancellationToken ct) =>
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
            var allowed = (order.Status, status) switch
            {
                (OrderStatus.Paid, OrderStatus.Preparing) => true,
                (OrderStatus.Preparing, OrderStatus.Ready) => true,
                (OrderStatus.Ready, OrderStatus.Completed) => true,
                (OrderStatus.Pending, OrderStatus.Cancelled) => true,
                _ => false
            };
            if (!allowed)
                return Results.Problem(statusCode: 409, title: "Status transition not allowed",
                    detail: "Use Paid -> Preparing -> Ready -> Completed. Only a Pending order with a confirmed declined payment may be cancelled. Paid is set through payment confirmation only.");

            if (status == OrderStatus.Cancelled)
            {
                PaymentRecord? payment;
                try { payment = await payments.GetAsync(order, ct); }
                catch (PaymentUnknownException ex) { return PaymentWorkflow.Unknown(order, ex.Message); }
                if (payment?.Status != "Declined")
                    return Results.Problem(statusCode: 409, title: "Cancellation requires a confirmed declined payment",
                        detail: "An uncertain payment may have succeeded. Reconcile the order first. Refunds are outside this simulation.");
            }

            // Compare-and-update prevents racing requests from overwriting a newer status.
            var oldStatus = order.Status;
            var changed = await db.Orders.Where(x => x.OrderId == id && x.Status == oldStatus)
                .ExecuteUpdateAsync(updates => updates.SetProperty(x => x.Status, status), ct);
            if (changed == 0)
                return Results.Problem(statusCode: 409, title: "Order status changed; retrieve the order and retry.");
            order.Status = status;
            app.Logger.LogInformation("Order {OrderId} status changed from {OldStatus} to {Status}", id, oldStatus, order.Status);
            return Results.Ok(order);
        });
    }
}
