# Student extension: InventoryService

## Objective

Extend the working Seekers Café demo with one independent business capability:
stock availability. Use C#, ASP.NET Core Minimal APIs, EF Core, SQLite and HTTP/JSON.
The base application is an educational simulation, not an official ordering or
payment system. InventoryService is **not implemented** in the supplied baseline:
there is no project, database or running endpoint for it yet.

Read the [architecture overview](architecture.md), then trace one checkout in
`src/OrderService/OrderEndpoints.cs`. Preserve the existing service boundaries.

## Core assignment

1. Create an independent `src/InventoryService` project and add it to the solution.
   Give it its own local port, configuration and `inventory.db`. A suggested local
   port is 5104; choose another if necessary and document it.
2. Model one stock record per external `MenuItemId`, with a unique menu ID and a
   nonnegative integer quantity. Inventory owns stock counts. It does not own menu
   names or prices and has no foreign key into `menu.db`.
3. Expose APIs to retrieve stock and update/restock quantities. Validate IDs,
   missing records and quantities; use appropriate HTTP results. Initialize
   illustrative stock for known menu IDs so the normal checkout can be demonstrated.
4. Add an `IHttpClientFactory` client to OrderService with a configurable base URL
   and finite timeout. Call InventoryService after menu validation and before the
   Pending order is saved or payment is attempted. The integration point is marked
   in the existing endpoint. Use HTTP, not another service's DbContext or database.
5. Check **every** requested line against its requested quantity. Reject an order
   if any product has insufficient stock. A missing stock record must not silently
   mean unlimited stock. Treat an unavailable or timed-out inventory dependency as
   a temporary checkout failure; do not accept an order using invented stock data.
6. Explain the relationship between MenuService's `IsAvailable` and stock. Keep
   `IsAvailable` as an ordering/menu gate; InventoryService becomes authoritative
   for quantities. An item must pass both checks. Existing order snapshots and
   server-calculated prices must continue to work.
7. Add its Dockerfile, Compose service, independent persistent volume and internal
   service URL. Keep the existing browser API contract if possible; the browser
   should still submit checkout to OrderService. Extend automated verification
   with inventory setup so normal existing checkout cases can still pass.

The core milestone validates current stock and supports explicit stock updates.
It does **not** claim safe stock reservation under concurrent checkouts. State
whether stock changes only through your update API or is also deducted during
checkout, and demonstrate that policy. If you deduct stock, explain the atomicity,
retry and payment-failure limitations of your approach; a negative count is never
an acceptable result. Use the extension below for stronger reservation guarantees.

## Proposed HTTP contract to discuss

These are suggested future routes, not existing baseline endpoints. You may choose
different names, but document the request/response shapes and error behavior.

| Proposed request | Responsibility |
| --- | --- |
| `GET /api/inventory` | List stock records for inspection |
| `GET /api/inventory/{menuItemId}` | Retrieve one product's count; 404 if unknown |
| `PUT /api/inventory/{menuItemId}` | Set a validated quantity for demonstration/restocking |

For the core exercise, OrderService can retrieve individual counts. Describe how
an aggregate check endpoint could reduce HTTP calls without promising reservation.
A suggested error convention is 400 for malformed input, 409 for insufficient stock,
404 for an unknown inventory record, and 503 when the dependency cannot be checked.
Distinguish the HTTP response from InventoryService from the customer-facing
response OrderService returns. Supply clear errors identifying affected menu IDs.

Do not copy all menu data into a shared business-model library or place stock
columns into MenuService merely to avoid an HTTP call. A common solution folder
or Compose network does not grant another service ownership of `inventory.db`.

## Acceptance demonstrations

Show the following with HTTP requests or repeatable integration checks:

- Positive stock permits a valid order and the existing payment workflow.
- Quantity zero rejects the product before order creation and payment.
- A request for three units when only two are available is rejected.
- A multi-item cart with one shortage is rejected as a whole before checkout
  persistence; the customer is not charged for the other lines.
- A missing inventory record produces a documented failure instead of unlimited stock.
- An item with stock but `IsAvailable: false` remains unorderable.
- InventoryService stopped, timed out or returning invalid data produces a clear
  failure; no Pending order or payment is created by that rejected checkout.
- Existing orders remain readable while InventoryService is offline.
- Invalid IDs, negative/fractional counts and malformed requests are rejected.
- Stock updates survive restarting/recreating the InventoryService container.
- Changing stock does not change existing receipt snapshots or menu prices.
- One service can be restarted without restarting the others, and only
  InventoryService mounts its volume.

Use isolated test databases/volumes. Document setup and expected HTTP outcomes;
do not weaken existing price, payment or state-transition checks to make new tests
pass. Seed stock for the menu IDs used in each isolated test instead of relying
on a separate classroom database's generated IDs.

## Design discussion: from a check to a reservation

Two concurrent orders can both observe stock of one and both proceed. Separate
HTTP reads do not prevent this. Even an atomic decrement must account for payment
declines, network failures and an OrderService crash after stock was changed.

As an optional extension, design and implement a **cart-level atomic reservation**:

- Reserve all requested lines or none in one InventoryService transaction.
- Use an operation/reservation identifier so retries do not deduct stock twice.
- Coordinate a stable identifier with the order workflow. The baseline does not
  have a saved order ID until Pending is persisted; decide whether to generate
  an earlier operation key or revise that workflow, and explain the trade-off.
- Confirm/consume a reservation only on a defined successful outcome.
- Release on confirmed failure/cancellation according to a documented policy.
  An unknown payment outcome is not proof that it is safe to release stock.
- Consider a lost reservation response, an abandoned checkout and a late payment.
  Explain whether expiry is safe and how reconciliation would work.
- Demonstrate two competing requests for the last unit: at most one succeeds,
  and retries do not oversell or double-release.

Keep this extension understandable with local EF transactions and explicit HTTP
contracts. A distributed transaction or message broker is not required. Clearly
state any recovery gaps rather than claiming an all-or-nothing transaction across
inventory, orders and payments.

## Submission and review checklist

Provide source/configuration, an updated architecture diagram, documented routes,
run/test commands, examples of successful and rejected checkout, and a short
explanation of ownership, failure handling and your stock-change policy. Identify
what you changed in OrderService and why consumers of unchanged contracts still work.

Review criteria the instructor can adapt:

- Independent responsibility, database and deployment.
- Clear API contracts, validation, async operations and dependency injection.
- Correct placement of the stock check and rejection before payment.
- Narrow changes to existing services without shared database access.
- Evidence of persistence, failure handling and meaningful integration checks.
- Honest discussion of concurrency, idempotency and recovery limitations.

The supplied baseline deliberately contains no InventoryService starter project
or implementation. Creating and integrating it is the student work.
