using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace PaymentService;

/// <summary>
/// Maps HTTP endpoints for retrieving and creating simulated payments, replaying matching duplicate
/// requests, rejecting conflicting inputs, and demonstrating declined payments or delayed responses.
/// </summary>
public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this WebApplication app, int responseDelay, bool declinePayments)
    {
        var payments = app.MapGroup("/api/payments");
        payments.MapGet("/{id:int}", async (int id, PaymentDbContext db, CancellationToken ct) =>
        {
            var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.PaymentId == id, ct);
            return payment is null ? Results.NotFound() : Results.Ok(payment);
        });
        payments.MapGet("/order/{orderId:int}", async (int orderId, PaymentDbContext db, CancellationToken ct) =>
        {
            var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
            return payment is null ? Results.NotFound() : Results.Ok(payment);
        });
        payments.MapPost("", async (PaymentRequest request, PaymentDbContext db, CancellationToken ct) =>
        {
            var errors = request.Validate();
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var existing = await FindExisting(db, request, ct);
            if (existing is not null) return Replay(existing, request);

            var payment = new Payment
            {
                OrderId = request.OrderId, RequestId = request.RequestId!, Amount = request.Amount!.Value,
                PaymentMethod = request.PaymentMethod!, Status = declinePayments ? "Declined" : "Succeeded",
                TransactionDate = DateTime.UtcNow
            };
            db.Payments.Add(payment);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
            {
                // Another request may have won the race after our initial lookup.
                db.ChangeTracker.Clear();
                existing = await FindExisting(db, request, ct);
                if (existing is null) throw;
                return Replay(existing, request);
            }
            app.Logger.LogInformation("Simulated payment {PaymentId} for order {OrderId}, request {RequestId}: {Status}",
                payment.PaymentId, payment.OrderId, payment.RequestId, payment.Status);
            // Deliberately delay AFTER commit to demonstrate a lost/late payment response.
            if (responseDelay > 0) await Task.Delay(responseDelay, ct);
            return Results.Created($"/api/payments/{payment.PaymentId}", payment);
        });
    }

    private static Task<Payment?> FindExisting(PaymentDbContext db, PaymentRequest request, CancellationToken ct) =>
        db.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.RequestId == request.RequestId || x.OrderId == request.OrderId, ct);

    private static IResult Replay(Payment payment, PaymentRequest request) =>
        payment.OrderId == request.OrderId && payment.RequestId == request.RequestId
        && payment.Amount == request.Amount && payment.PaymentMethod == request.PaymentMethod
            ? Results.Ok(payment)
            : Results.Problem(statusCode: 409, title: "Conflicting payment request",
                detail: "This order or request ID already has a payment with different inputs. No new payment was created.");
}
