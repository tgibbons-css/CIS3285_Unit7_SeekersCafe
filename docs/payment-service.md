# Phase 4: simulated payments

PaymentService owns `payments.db`. It simulates Credit Card and Dining Dollars
without contacting a provider or storing credentials. Start it with:

```powershell
dotnet run --project src/PaymentService --launch-profile http
```

It listens on `http://localhost:5103`. The first startup creates its schema.
It can run and be tested without MenuService or OrderService. Use
`requests/payments.http` for independent requests and the
[OrderService walkthrough](order-service.md) for full checkout.

## API and duplicate protection

| Request | Result |
| --- | --- |
| `POST /api/payments` | 201 for a new simulated result; 200 for an identical replay; 400 invalid input; 409 conflicting reuse |
| `GET /api/payments/{id}` | 200, saved payment; 404 if missing |
| `GET /api/payments/order/{orderId}` | 200, saved payment for an order; 404 if missing |

POST example (use a standalone demonstration order ID):

```json
{
  "orderId": 1000000,
  "requestId": "standalone-demo-1",
  "amount": 4.50,
  "paymentMethod": "Credit Card"
}
```

Order ID must be positive. Request ID must be nonblank, at most 100 characters,
and have no surrounding whitespace. Amount is required, from $0 to $1,000,000,
with at most two decimal places (matching the maximum validated cart total).
The method must be exactly `Credit Card` or `Dining Dollars`. No card or student
account fields exist. Unknown JSON properties are ignored and never persisted.

Responses contain `paymentId`, `orderId`, `requestId`, decimal-dollar `amount`,
`paymentMethod`, `status` (`Succeeded` or `Declined`) and UTC `transactionDate`.
Both success and decline are committed simulated results; a newly created decline
is also HTTP 201 here. OrderService translates that result into a checkout HTTP 402.

Separate database unique indexes protect **request ID** and **order ID**. Repeating
the same order, key, amount and method returns the original record, including an
original decline. Changing any input while reusing either ID returns 409. Database
constraints and a uniqueness-exception recovery path protect concurrent requests,
not just sequential replays. There is at most one immutable payment result per
order; this intentionally excludes retrying a decline with a new payment attempt.

PaymentService accepts amounts from its caller. It does not query OrderService or
verify that a standalone order exists. During checkout, OrderService is responsible
for server pricing. This keeps the simulation small and avoids a circular service
dependency; these unauthenticated APIs are not a production payment system.

## Checkout and reconciliation

1. OrderService validates input and retrieves menu products through HTTP.
2. It calculates a total and commits a Pending order plus line snapshots locally.
3. It calls PaymentService with the amount, selected method and a stable key:
   `order-<orderId>-<persisted UTC order-date ticks>`.
4. PaymentService saves exactly one Succeeded or Declined record.
5. On confirmed success, OrderService conditionally updates Pending to Paid and
   returns **201 `{order,payment}`**.
6. On confirmed decline, it retains Pending and returns **402 Problem Details**,
   including the saved order ID, order, payment status and payment ID.
7. On network failure, timeout or an invalid payment response, it retains Pending
   and returns **503 Payment outcome unknown**, including the order and a reconciliation URL.

Reconcile a retained order with:

```powershell
Invoke-RestMethod http://localhost:5102/api/orders/1/payment/reconcile -Method Post
```

This reads PaymentService's payment-by-order endpoint, verifies the order ID,
stable request key, amount, supported method and terminal payment status, and
applies only confirmed success. It never POSTs a payment. A missing record returns
409 without changing the order; absence is not proof that an in-flight payment
will never commit. Repeating reconciliation is safe and cannot regress fulfillment.

An unknown order cannot be cancelled until its payment outcome is known. A
confirmed decline permits Pending to Cancelled; Paid orders proceed through
Preparing, Ready and Completed. Callers cannot set Pending to Paid directly.

## Classroom demonstrations

For normal success, run all three services with defaults and send the checkout in
`requests/orders.http`. Switch between the two simulated method names.

For a decline, stop PaymentService with Ctrl+C, then restart it:

```powershell
dotnet run --project src/PaymentService --launch-profile http -- --Simulation:DeclinePayments true
```

Place a new order. Checkout returns 402 and the order stays Pending. Inspect the
payment by order ID and demonstrate that preparation is blocked. Replaying an
existing payment does not change its recorded result when configuration changes.

For an outage, stop PaymentService and place a new order. The menu is validated
and Pending is saved, then checkout returns 503 with the order ID. GET order still
works. Restart PaymentService and reconcile: if no payment was saved, the response
is 409 and no payment is initiated. An explicit recovery/retry workflow for this
case is outside the initial implementation; don't resubmit checkout as a status check.

For the most useful failure lesson, simulate a committed payment with a late response:

```powershell
# Stop the running PaymentService first. Restart it with a 7-second post-commit delay:
dotnet run --project src/PaymentService --launch-profile http -- --Simulation:ResponseDelayMilliseconds 7000
```

OrderService's default payment timeout is five seconds. Place a new order: payment
commits, checkout times out, and the saved order remains Pending. Look up the
payment by order ID to see Succeeded. Reconcile the order and observe Paid with
the same payment ID. Restart OrderService before reconciling to show that recovery
uses persisted order fields. Repeating reconciliation or replaying the identical
payment request creates no second payment. Remove the command-line option on the
next restart to restore normal response timing. Delay affects only new payments;
replays and GETs return immediately.

## Configuration and source

Defaults in `src/PaymentService/appsettings.json`:

```json
{
  "ConnectionStrings": { "PaymentDatabase": "Data Source=payments.db" },
  "Simulation": { "DeclinePayments": false, "ResponseDelayMilliseconds": 0 }
}
```

Delay must be 0–10000 milliseconds. Flags are read on startup. Environment overrides
use `Simulation__DeclinePayments`, `Simulation__ResponseDelayMilliseconds` and
`ConnectionStrings__PaymentDatabase`. The normal project-local path is
`src/PaymentService/payments.db`. Its parent must exist and be writable. Only
PaymentService accesses this file. There is no browser CORS policy here because
the frontend will call OrderService for checkout, not PaymentService directly.

`Program.cs` configures EF Core, schema creation, Problem Details and scoped JSON
console logs. `Payment.cs` defines the record and owning DbContext, exact integer
cent mapping and unique indexes. `PaymentRequest.cs` validates input.
`PaymentEndpoints.cs` handles simulation and replay/conflict behavior. Its optional
delay is deliberately after `SaveChangesAsync` to expose a distributed failure
window. Logs identify order/payment/request IDs and outcomes without request bodies.

## Limits to explain in class

Orders and payments commit in separate local transactions. There is **no atomic
cross-service transaction**. Payment may succeed even when checkout returns 503,
the caller disconnects, or OrderService crashes before saving Paid. A failure of
the local Paid update can also produce a server error after payment succeeds.
Reconciliation repairs an existing confirmed result; it is manual, not a durable
background recovery system. It cannot fix every failure automatically.

OrderService does not persist a payment outcome column: it retains Pending and
rechecks the authoritative payment record when needed. Phase 3 order data therefore
needs no schema upgrade; legacy Pending orders without payments remain unpaid.
The stable key survives service restarts. PaymentService also enforces numeric
order ID uniqueness, so reset order/payment databases together for disposable
demos, or retain both. Resetting only orders can cause ID collisions and 409s.

Deduplication protects one order's repeated payment request. It does not protect
repeated checkout POSTs, which create separate orders. New payment attempts after
decline, refunds, authorization, real payment providers, distributed transactions,
durable messaging and automatic reconciliation are deliberately excluded.

## Integration checks

Build the solution, then run:

```powershell
./scripts/test-payments.ps1
./scripts/test-orders.ps1
```

The payment suite starts PaymentService independently, then exercises integrated
checkout using three isolated real SQLite databases. It checks input errors,
conflicting keys/amounts/methods, concurrent duplicate submissions, restart-safe
replays, both payment methods, declines, cancellation guards, fulfillment,
outage uncertainty, a payment committed before a timeout, and reconciliation
after an OrderService restart. Test processes are stopped afterward. Test databases
and logs remain under `.local/payment-tests/` for inspection.

Phase 4 verification: the solution builds with zero warnings and errors. All
73 payment checks and 55 order checks pass in both Development and Production.
The payment suite also rejects upstream HTTP 500, malformed JSON, missing fields
and inconsistent payment data without incorrectly marking an order Paid. JSON
logs were inspected for payment/order/request IDs. Normal demo databases were
not modified, and the test processes were stopped afterward.
