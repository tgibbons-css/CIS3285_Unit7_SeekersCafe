# Phase 3: OrderService

OrderService owns `orders.db` and communicates with MenuService only through HTTP.
It does not reference MenuService's C# project, entity classes or database. Its
consumer-side `MenuProduct` record describes the JSON fields it needs.

## Run and place an order

Build the solution, then use two terminals:

```powershell
dotnet restore SeekersCafe.sln
dotnet build SeekersCafe.sln --no-restore
dotnet run --project src/MenuService --launch-profile http
```

```powershell
dotnet run --project src/OrderService --launch-profile http
```

Use `requests/orders.http` with VS Code REST Client, or in a third terminal:

```powershell
Invoke-RestMethod http://localhost:5101/api/menu | Format-Table
$body = @{
    customerName = 'Classroom Customer'
    items = @(
        @{ menuItemId = 1; quantity = 2 }
        @{ menuItemId = 5; quantity = 1 }
    )
} | ConvertTo-Json -Depth 5
$order = Invoke-RestMethod http://localhost:5102/api/orders -Method Post -ContentType 'application/json' -Body $body
$order | ConvertTo-Json -Depth 5
Invoke-RestMethod "http://localhost:5102/api/orders/$($order.orderId)"
```

With unchanged sample data, two Lattes and one Blueberry Muffin total **$12.00**.
The server returns a generated ID, UTC `orderDate`, trimmed `customerName`,
`status: "Pending"`, `totalAmount`, and `orderItems`. Each line contains its
generated `orderItemId`, owning `orderId`, external `menuItemId`, snapshotted
`itemName`, `quantity` and `unitPrice`. Get current IDs from the menu if it has
been edited. PaymentService is not needed and is not called in Phase 3.

## API contract

| Request | Success | Errors |
| --- | --- | --- |
| `GET /api/orders` | 200, newest-first array including item snapshots | 500 for unexpected local failures |
| `GET /api/orders/{id}` | 200, complete order | 404 if missing |
| `POST /api/orders` | 201, Pending order and Location header | 400 invalid input/missing or unavailable product; 503 failed menu dependency |
| `PUT /api/orders/{id}/status` | 200, resulting order | 400 invalid status name; 404 missing order; 409 forbidden or conflicting transition |

POST accepts `customerName` and `items` only. Each input line accepts `menuItemId`
and `quantity`. Extra properties, including client prices, names, total, status,
ID and date, are ignored. They never determine stored values.

- Customer name is required, nonblank and at most 100 trimmed characters.
- Supply 1–50 distinct menu item lines; null lines are invalid.
- IDs must be positive integers and quantities must be integers from 1 through 20.
- Duplicate menu IDs are rejected: combine their quantities into one line.
- The server retrieves every product, verifies availability, and uses decimal
  arithmetic to calculate the total. It stores integer cents for exact money values.
- Malformed JSON or incompatible property types return 400. Field errors use
  Problem Details with an `errors` dictionary; menu item errors identify the input index.

Status requests use a case-sensitive name, for example:

```json
{ "status": "Cancelled" }
```

The six status names are Pending, Paid, Preparing, Ready, Completed and Cancelled.
For Phase 3, **Pending → Cancelled is the only change allowed**. Repeating the
current status returns 200 without changing anything. Other transitions return
409, so a caller cannot mark an unpaid order Paid or start preparation. Cancelled
orders cannot be reopened. The cancellation update checks the old status atomically
so concurrent requests cannot silently overwrite a newer status. Phase 4 will
extend transitions after payment confirmation.

## Checkout flow and source files

`Program.cs` registers a scoped `OrderDbContext`, typed `MenuClient` through
`AddHttpClient`, Problem Details, JSON logging and local frontend CORS. It creates
the order schema before listening without needing MenuService to be online.

`OrderRequests.cs` defines and validates the input contracts. `OrderEndpoints.cs`
first validates input, then asks `MenuClient.cs` to fetch products. The client uses
one overall deadline for all menu lookups (five seconds by default), propagates
request cancellation, handles missing products separately, and rejects malformed
or inconsistent dependency responses. `IHttpClientFactory` manages HTTP client
handlers and provides outgoing request logs. There are no automatic retries.

After all menu checks pass, the endpoint constructs an order with historical
snapshots. `OrderDbContext.cs` saves the order and its lines in one local transaction.
There are no cross-service foreign keys: deleting or editing a menu product leaves
existing order snapshots intact. Reads use only the order database and remain
available during a MenuService outage. `Order.cs` contains the persistence entities
and status names; no shared business-model library is introduced.

Structured console logs include outgoing menu item IDs and incoming request
method/path/status/duration. The creation log includes the generated order ID,
line count, total and status. Cancellation logs include the order ID. Request bodies
and customer names are not logged. Request tracing is available through ASP.NET
Core/HttpClient log scopes, while the persistent order ID is assigned after validation.

## Failure demonstrations

1. Create an order and note its ID and total.
2. Change a product name/price with MenuService PUT. Retrieve the original order:
   its snapshots stay unchanged. A subsequent order uses the updated price.
3. Mark a product unavailable, then try ordering it: 400 and no new order.
4. Stop MenuService, keeping OrderService running. Create an order: 503 Problem
   Details explains the dependency failure and that no order was created. Input
   validation still works, and existing orders can still be read or cancelled.
5. Restart MenuService and submit again: checkout recovers without restarting
   OrderService. Successful requests are separate orders; no submission key exists yet.
6. Stop and restart OrderService: orders, line snapshots and cancellation persist.

The integration script additionally simulates HTTP 500, invalid JSON, missing
contract fields, wrong product IDs and an unresponsive menu server. All return
503 and leave the order database unchanged. Dependency errors do not leak upstream
response bodies. A timeout uses a specific explanation in Problem Details.

## Configuration and persistence

Defaults in `src/OrderService/appsettings.json`:

```json
{
  "ConnectionStrings": { "OrderDatabase": "Data Source=orders.db" },
  "MenuService": { "BaseUrl": "http://localhost:5101/", "TimeoutSeconds": 5 }
}
```

Relative database paths resolve against OrderService's content root. `dotnet run
--project src/OrderService` therefore creates `src/OrderService/orders.db`. The
database parent directory must already exist and be writable. `EnsureCreatedAsync`
does not update existing schemas; use disposable demo databases or introduce
migrations deliberately before retaining data across model changes.

Environment overrides use double underscores, for example `MenuService__BaseUrl`,
`MenuService__TimeoutSeconds` and `ConnectionStrings__OrderDatabase`. The base URL
must be absolute HTTP(S), and timeout must be 1–30 seconds. The HTTP client calls
the root route `/api/menu/{id}` on that host. Docker-specific overrides and storage
volumes are deferred to Phase 6.

## Automated checks

After building Debug configuration:

```powershell
./scripts/test-orders.ps1
# If the defaults are occupied:
./scripts/test-orders.ps1 -MenuPort 5293 -OrderPort 5294
```

The script uses isolated real SQLite files under `.local/order-tests/<run>/`,
starts only MenuService and OrderService, and cleans up its processes. It leaves
database files and logs for inspection without editing the normal demo databases.
It verifies server pricing despite forged client fields, snapshots after menu
changes/deletion, validation, status restrictions, CORS, restart persistence,
outages, contract faults, timeouts and recovery. The fault responder exists only
inside this script; no demonstration failure endpoint is added to the application.

Phase 3 verification: the solution builds with zero warnings and errors. All
53 HTTP checks pass in both Development and Production. JSON logs were inspected
to confirm order IDs, totals, statuses and request trace scopes. Test processes
were stopped afterward; the regular classroom databases were not changed.

## Intentional limits and next integration point

Orders are Pending and no payment is simulated in this phase. Prices and
availability can change after validation; the system does not reserve stock.
Multiple lookups also do not form a transactional snapshot of the menu. The list
endpoint is unpaged for a small classroom dataset. Local mutation endpoints are
unauthenticated; CORS is not access control.

A request may commit locally and then lose its response; retrying POST creates
another order. There is no order-submission idempotency or distributed transaction.
Phase 4 will call PaymentService after saving Pending and update the order only
on confirmed payment. A future InventoryService check belongs after menu
validation and before persistence; no inventory project or implementation is added.
