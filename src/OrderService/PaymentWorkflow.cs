using Microsoft.EntityFrameworkCore;

namespace OrderService;

/// <summary>
/// Applies confirmed payment success to pending orders without overwriting later workflow states
/// and builds responses that direct clients to reconcile uncertain payment outcomes.
/// </summary>
public static class PaymentWorkflow
{
    public static async Task ApplyConfirmedAsync(Order order, PaymentRecord payment, OrderDbContext db, CancellationToken ct)
    {
        if (payment.Status != "Succeeded") return;
        // A local conditional update cannot regress preparation, completion or cancellation.
        var changed = await db.Orders.Where(x => x.OrderId == order.OrderId && x.Status == OrderStatus.Pending)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, OrderStatus.Paid), ct);
        if (changed > 0) order.Status = OrderStatus.Paid;
        else order.Status = await db.Orders.Where(x => x.OrderId == order.OrderId).Select(x => x.Status).SingleAsync(ct);
    }

    public static IResult Unknown(Order order, string detail) => Results.Problem(statusCode: 503,
        title: "Payment outcome unknown", detail: detail + $" The order remains {order.Status}. Reconcile this order; do not resubmit checkout.",
        extensions: new Dictionary<string, object?>
        {
            ["orderId"] = order.OrderId, ["paymentStatus"] = "Unknown", ["order"] = order,
            ["reconcileUrl"] = $"/api/orders/{order.OrderId}/payment/reconcile"
        });
}
