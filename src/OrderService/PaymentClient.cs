using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrderService;

/// <summary>
/// Represents the payment details returned by PaymentService for checkout and payment reconciliation.
/// </summary>
public sealed record PaymentRecord(
    [property: JsonRequired] int PaymentId, [property: JsonRequired] int OrderId,
    [property: JsonRequired] string RequestId, [property: JsonRequired] decimal Amount,
    [property: JsonRequired] string PaymentMethod, [property: JsonRequired] string Status);

/// <summary>
/// Indicates that a payment's outcome cannot be confirmed, so the order requires reconciliation.
/// </summary>
public sealed class PaymentUnknownException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Submits simulated payments with a stable request identifier and retrieves existing payment results over HTTP,
/// validating that each response matches the order.
/// </summary>
public sealed class PaymentClient(HttpClient client)
{
    // Stable across restarts and retries, using only persisted order fields (no schema change).
    public static string RequestId(Order order) => FormattableString.Invariant($"order-{order.OrderId}-{order.OrderDate.Ticks}");

    public async Task<PaymentRecord?> GetAsync(Order order, CancellationToken ct) =>
        await SendAsync(order, null, ct);

    public async Task<PaymentRecord> PayAsync(Order order, string method, CancellationToken ct) =>
        (await SendAsync(order, method, ct))!;

    private async Task<PaymentRecord?> SendAsync(Order order, string? method, CancellationToken ct)
    {
        try
        {
            using var response = method is null
                ? await client.GetAsync($"/api/payments/order/{order.OrderId}", ct)
                : await client.PostAsJsonAsync("/api/payments", new
                {
                    orderId = order.OrderId, requestId = RequestId(order),
                    amount = order.TotalAmount, paymentMethod = method
                }, ct);
            if (method is null && response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            var payment = await response.Content.ReadFromJsonAsync<PaymentRecord>(ct);
            if (payment is null || payment.PaymentId <= 0 || payment.OrderId != order.OrderId
                || payment.RequestId != RequestId(order) || payment.Amount != order.TotalAmount
                || payment.PaymentMethod is not ("Credit Card" or "Dining Dollars")
                || (method is not null && payment.PaymentMethod != method)
                || payment.Status is not ("Succeeded" or "Declined"))
                throw new PaymentUnknownException("PaymentService returned inconsistent payment data.");
            return payment;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        { throw new PaymentUnknownException("PaymentService timed out; the payment may already be saved.", ex); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
        { throw new PaymentUnknownException("PaymentService could not confirm the payment outcome.", ex); }
    }
}
