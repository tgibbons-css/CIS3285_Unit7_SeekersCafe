# OrderService — through Phase 4

OrderService owns `orders.db`, retrieves products from MenuService through HTTP,
and calls PaymentService after saving a Pending order. It does not reference
either service's C# project, entities or database. The consumer-owned `MenuProduct`
and `PaymentRecord` records describe the JSON fields it needs.

## Run and place an order

Build, then run the services in three separate terminals:

```powershell
dotnet restore SeekersCafe.sln
dotnet build SeekersCafe.sln --no-restore
dotnet run --project src/MenuService --launch-profile http
```

```powershell
dotnet run --project src/PaymentService --launch-profile http
```

```powershell
dotnet run --project src/OrderService --launch-profile http
```

Use `requests/orders.http`, or in another terminal:

```powershell
Invoke-RestMethod http://localhost:5101/api/menu | Format-Table
$body = @{
    customerName = 'Classroom Customer'
    paymentMethod = 'Credit Card'
    items = @(
        @{ menuItemId = 1; quantity = 2 }
        @{ menuItemId = 5; quantity = 1 }
    )
} | ConvertTo-Json -Depth 5
$checkout = Invoke-RestMethod http://localhost:5102/api/orders -Method Post -ContentType 'application/json' -Body $body
$checkout | ConvertTo-Json -Depth 5
Invoke-RestMethod "http://localhost:5102/api/orders/$($checkout.order.orderId)"
```

With unchanged sample data, two Lattes and one Blueberry Muffin total **$12.00**.
Get current IDs from the menu if it has been edited. Successful checkout now
returns **`{ "order": { ... }, "payment": { ... } }`**, with order status Paid.
This differs from Phase 3's bare Pending order response. GET order endpoints still
return the bare order with its saved line snapshots. No payment credentials are needed.

## API contract

| Request | Result |
| --- | --- |
| `GET /api/orders` | 200, newest-first array with saved item snapshots |
| `GET /api/orders/{id}` | 200, complete order; 404 if missing |
| `POST /api/orders` | 201 `{order,payment}` on success; 400 invalid input/product; 402 confirmed decline; 503 dependency failure |
| `PUT /api/orders/{id}/status` | 200, resulting order; 400 invalid status; 404 missing; 409 forbidden/conflicting transition |
| `POST /api/orders/{id}/payment/reconcile` | 200 `{order,payment}` for a confirmed record; 404 missing order; 409 no payment record; 503 unknown outcome |

Menu failures happen before order creation. Payment failures happen after the
Pending order is saved. A 402 or payment-related 503 includes `orderId`, `order`
and `paymentStatus` in Problem Details. Unknown outcomes also include `reconcileUrl`.
Retain that order ID; do not submit checkout again to discover its status.
See the [payment workflow and failure walkthrough](payment-service.md).

POST accepts a customer name, simulated payment method and item IDs/quantities.
Extra properties such as client prices, item names, total, status, ID and date
are ignored; the server supplies all of them.

- Customer name is required, nonblank and at most 100 trimmed characters.
- `paymentMethod` must be exactly `Credit Card` or `Dining Dollars`.
- Supply 1–50 distinct lines, each with a positive `menuItemId` and integer
  `quantity` from 1 through 20. Null lines and duplicate IDs are rejected.
- Products must exist and be available. Prices are retrieved from MenuService,
  calculated using decimal arithmetic and stored as exact integer cents.
- Malformed JSON and incompatible property types return 400. Field validation
  errors use Problem Details with an `errors` dictionary and input line indexes.

## Status transitions

Only payment confirmation or reconciliation sets Pending to Paid. Staff use:

```text
Paid -> Preparing -> Ready -> Completed
```

Status requests contain a case-sensitive name, e.g. `{ "status": "Preparing" }`.
Repeating the current status is a 200 no-op. Skipping or reversing steps returns
409. A Pending order may become Cancelled only after PaymentService confirms a
decline. Unknown or missing payment outcomes cannot be cancelled: a payment could
still be in flight. Paid orders cannot be cancelled because refunds are outside
this simulation. Cancelled and Completed are terminal.

Updates compare the old status in the SQL WHERE clause, so racing requests cannot
silently overwrite a newer status. Reconciliation never regresses a prepared,
completed or cancelled order.

## Implementation and ownership

`Program.cs` registers the scoped `OrderDbContext`, typed HTTP clients, JSON
logging, Problem Details and local frontend CORS. Schema initialization needs no
dependency online. HTTP base URLs and finite timeouts come from configuration.
`IHttpClientFactory` manages handlers and outgoing logs; no automatic retry policy
is installed. Menu validation has one overall deadline for all item lookups.

`OrderRequests.cs` validates inputs. `OrderEndpoints.cs` constructs historical
snapshots only after menu checks pass. `OrderDbContext.cs` saves order and lines
in one local transaction. `PaymentClient.cs` sends the server amount and a stable
request key; `PaymentWorkflow.cs` applies only a confirmed success through a local
conditional update. There is no transaction across the two databases.

Each order exposes its ID, trimmed customer name, UTC date, status, total and
`orderItems`. Each saved line has its own ID, owning order ID, external menu ID,
name, quantity and unit price. There is no foreign key into MenuService. Menu edits
or deletion cannot rewrite history. GET orders uses only `orders.db` and remains
available while either dependency is offline. Payment details are retrieved on
reconciliation rather than copied into the order schema.

Structured logs show incoming method/path/status/duration, outgoing HTTP calls,
order/payment IDs and payment request keys. Request bodies and customer names are
not logged. JSON log scopes include trace IDs for following a checkout request.

## Configuration and persistence

```json
{
  "ConnectionStrings": { "OrderDatabase": "Data Source=orders.db" },
  "MenuService": { "BaseUrl": "http://localhost:5101/", "TimeoutSeconds": 5 },
  "PaymentService": { "BaseUrl": "http://localhost:5103/", "TimeoutSeconds": 5 }
}
```

Environment overrides use double underscores, e.g. `PaymentService__BaseUrl` and
`ConnectionStrings__OrderDatabase`. Dependency URLs must be absolute HTTP(S), with
API paths at the host root. Timeouts must be 1–30 seconds. Relative database paths
resolve against the content root: normal `dotnet run --project src/OrderService`
creates `src/OrderService/orders.db`. Parent directories must exist and be writable.

Phase 4 does not change the Phase 3 order schema or reset existing data. Historical
Pending orders with no payment remain Pending; reconciliation reports no record
and never initiates a payment. `EnsureCreatedAsync` still does not upgrade schemas;
introduce migrations deliberately before future model changes need to retain data.
Keep order and payment databases together when retaining or resetting the demo:
PaymentService enforces one payment per numeric order ID and will reject collisions
if only the order database is reset. Docker storage/configuration comes in Phase 6.

## Tests and intentional limits

After building Debug configuration:

```powershell
./scripts/test-orders.ps1
./scripts/test-payments.ps1
```

Both scripts use isolated real SQLite databases under `.local`, stop their own
service processes, and leave logs for inspection. The order suite now starts all
three services and tests server pricing, snapshot history, validation, CORS,
fulfillment transitions, persistence, menu outages/contract faults and recovery.
The payment suite covers declines, concurrent duplicate protection and uncertain
outcomes followed by reconciliation. Ports can be overridden via script parameters.

Prices and availability may change between validation and payment; there is no
stock reservation or transactional menu snapshot. Orders are unpaged for classroom
datasets. The APIs have no authentication; local CORS is not access control. Retrying
checkout creates a different order, even if the original response was lost. Payment
deduplication does not prevent that. No automatic reconciliation, distributed
transaction, durable messaging, refund flow or retry of a declined payment is added.
A future InventoryService check belongs before saving Pending; no inventory
implementation or frontend is included yet.
