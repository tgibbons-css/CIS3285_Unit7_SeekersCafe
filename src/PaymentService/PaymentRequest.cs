namespace PaymentService;

// No card numbers, student account numbers, or other credentials are accepted or stored.
public sealed record PaymentRequest(int OrderId, string? RequestId, decimal? Amount, string? PaymentMethod)
{
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (OrderId <= 0) errors["orderId"] = ["Supply a positive order ID."];
        if (string.IsNullOrWhiteSpace(RequestId) || RequestId.Length > 100 || RequestId != RequestId.Trim())
            errors["requestId"] = ["Supply a nonblank request ID of at most 100 characters without surrounding whitespace."];
        if (Amount is null || Amount < 0 || Amount > 1000000 || decimal.Round(Amount.Value, 2) != Amount.Value)
            errors["amount"] = ["Amount is required, from 0 to 1000000 dollars, with at most two decimal places."];
        if (PaymentMethod is not ("Credit Card" or "Dining Dollars"))
            errors["paymentMethod"] = ["Choose Credit Card or Dining Dollars. These are simulated methods only."];
        return errors;
    }
}
