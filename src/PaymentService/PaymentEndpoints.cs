using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace PaymentService;

/// <summary>
/// Maps HTTP endpoints for retrieving and creating simulated payments, replaying matching duplicate
/// requests, rejecting conflicting inputs, and demonstrating declined payments or delayed responses.
/// EF Core is the ORM (object-relational mapper) used to read and write this service's SQLite data
/// through PaymentDbContext; it translates LINQ queries and entity changes into SQL operations.
/// This API creates and reads payment records; it does not expose update or delete endpoints.
/// </summary>
public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this WebApplication app, int responseDelay, bool declinePayments)
    {
        var payments = app.MapGroup("/api/payments");
        payments.MapGet("/{id:int}", async (int id, PaymentDbContext db, CancellationToken ct) =>
        {
            // READ by payment primary key. No tracking is needed because this endpoint only returns data.
            var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.PaymentId == id, ct);
            return payment is null ? Results.NotFound() : Results.Ok(payment);
        });
        payments.MapGet("/order/{orderId:int}", async (int orderId, PaymentDbContext db, CancellationToken ct) =>
        {
            // READ the payment associated with an order; the unique database index allows at most one.
            var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
            return payment is null ? Results.NotFound() : Results.Ok(payment);
        });
        payments.MapPost("", async (PaymentRequest request, PaymentDbContext db, CancellationToken ct) =>
        {
            // Validate before writing, then check whether a retry is an exact replay or a conflict.
            var errors = request.Validate();
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var existing = await FindExisting(db, request, ct);
            if (existing is not null) return Replay(existing, request);

            // CREATE a new payment entity from validated values. Add marks it for insertion;
            // SaveChangesAsync performs the INSERT and obtains the generated payment ID.
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

    // Look for a retry by either unique key. AsNoTracking is appropriate because this is a read-only check.
    private static Task<Payment?> FindExisting(PaymentDbContext db, PaymentRequest request, CancellationToken ct) =>
        db.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.RequestId == request.RequestId || x.OrderId == request.OrderId, ct);

    // Return the saved result for an identical retry; reject reuse of an order/request ID with changed data.
    private static IResult Replay(Payment payment, PaymentRequest request) =>
        payment.OrderId == request.OrderId && payment.RequestId == request.RequestId
        && payment.Amount == request.Amount && payment.PaymentMethod == request.PaymentMethod
            ? Results.Ok(payment)
            : Results.Problem(statusCode: 409, title: "Conflicting payment request",
                detail: "This order or request ID already has a payment with different inputs. No new payment was created.");
}
