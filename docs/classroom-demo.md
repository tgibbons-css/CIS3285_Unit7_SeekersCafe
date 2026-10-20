# Classroom Demonstration

Instructor walkthrough for CIS 3285: Advanced Software Design, College of
St. Scholastica. This is an educational simulation with illustrative products and
payments. It does not connect to campus ordering, financial or student systems.

The seven development phases are complete. InventoryService is the next student
assignment, not an implemented part of this application.

## Before class

Use one PowerShell terminal for all Compose commands and simulation variables.
Examples assume the default host ports 5100–5103. If using custom ports, adjust
the URLs in `requests/classroom.http` and the preflight parameters accordingly.
Stop local `dotnet run` copies using those ports before starting containers.

```powershell
$env:SIMULATE_DECLINE = 'false'
$env:PAYMENT_DELAY_MS = '0'
docker compose up --build -d
docker compose ps
./scripts/check-demo.ps1
```

The preflight is read-only. It checks all four applications, available menu items,
order reads, frontend HTML/configuration and the APIs' browser CORS origin. It
does not process a payment or prove simulation settings; inspect them separately:

```powershell
docker compose exec payment printenv Simulation__DeclinePayments Simulation__ResponseDelayMilliseconds
```

Expected values are `false` and `0`. Wait for readiness before class; the first
image download/build can take longer than the presentation. If the menu is empty,
add/enable sample products through MenuService rather than deleting databases.
There is no need to reset retained data. The REST walkthrough creates its own
dedicated sample product and captures generated IDs instead of assuming seed IDs.

Open these in advance:

- The app at **http://localhost:5100**.
- [Architecture overview](architecture.md), especially the service diagram.
- `requests/classroom.http` in VS Code with REST Client.
- `src/OrderService/OrderEndpoints.cs` and `src/OrderService/PaymentClient.cs`.
- `docker-compose.yml`, plus the service Dockerfiles if discussing build layers.

Send the REST requests individually. Every checkout POST creates a different
order; GET is for retrieval. The explicitly labeled payment replay is the only
payment POST in this walkthrough meant to be repeated with identical inputs.

## Suggested 30-minute sequence

| Minutes | Demonstration | Main concept |
| --- | --- | --- |
| 0–3 | Show the diagram and service/database ownership | Business boundaries; four processes, three business services |
| 3–8 | Order in the UI; run the API price-authority example | HTTP contracts; server validation and calculation |
| 8–11 | Replay payment; edit a menu product; retrieve old order | Idempotent payment; historical snapshots; loose coupling |
| 11–15 | Stop/start MenuService; inspect logs | Independent deployment; partial failure; observability |
| 15–18 | Turn on simulated declines | Business failure differs from network uncertainty |
| 18–23 | Delay payment response; reconcile the saved order | Separate commits; unknown outcome; no distributed transaction |
| 23–26 | Recreate containers and show retained history | Containers are replaceable; named volumes own persistent data |
| 26–30 | Introduce InventoryService and discuss stock races | Extensibility; new data owner; distributed consistency trade-offs |

### 1. Explain the working boundaries

WebApp serves static files. The browser calls MenuService and OrderService through
HTTP/JSON. OrderService calls MenuService for product validation and PaymentService
for simulated payment. Each business service has its own DbContext and SQLite
database; no other service reads that file. Cross-service IDs are API data, not
database foreign keys. Sharing a Docker network does not share a database.

Show the three volume declarations and exclusive mounts in Compose. Distinguish
host ports 5101–5103 from container port 8080. OrderService uses `menu:8080` and
`payment:8080`; the browser receives `localhost` URLs from `/api/config`.

Question: “If we changed MenuService's internal table layout while keeping its JSON
contract stable, which consumer code would have to change?” Expected answer:
none solely because of that internal database change.

### 2. Walk through a paid order and server authority

In the UI, add products, adjust quantities, enter a demonstration name and select
either simulated payment method. Place the order and note its number and confirmed
total. Use the staff demo to advance it through Preparing, Ready and Completed;
refresh the customer's status. No real payment details are requested.

Then run `classroom.http` requests 1–4. They create a **Classroom Special** priced
at $3.25, order two units, retrieve the order and retrieve its payment. The checkout
deliberately includes forged item price/name, total and status fields. Expect a
201 response with server total **$6.50**, saved item name Classroom Special, and
status **Paid**, not the requested Completed. Names, prices and statuses do not
come from those extra client properties.

In `OrderEndpoints.cs`, follow input validation, MenuClient HTTP retrieval,
server-side calculation, the local Pending save, payment POST, and the conditional
Paid update. These are separate local transactions. Show `CreateOrderRequest`:
its shape contains only editable inputs. Request 9 demonstrates a validation 400
that creates neither an order nor a payment.

### 3. Show replay protection and snapshots

Send requests 5–6. The exact payment replay returns 200 with the original payment
ID. The conflicting amount returns 409. Show PaymentService's unique indexes on
request ID and order ID, and its recovery from a concurrent uniqueness conflict.
Explain that this does not deduplicate two separate checkout requests.

Send requests 7–8. The menu product now has a new name and $4.00 price. The old
order retains its original name, $3.25 unit price and $6.50 total. Explain why
OrderService stores snapshots instead of joining live menu data for past orders.
A later order for one of this product totals $4.00; earlier receipts stay unchanged.

### 4. Make a dependency disappear

```powershell
docker compose stop menu
docker compose ps -a
```

Retrieve the previously saved order: it still works. PaymentService and WebApp
remain up. Refresh the menu in the page: it reports the failure and disables
checkout. Send REST request 10 to see the server-side protection too: 503,
MenuService unavailable, no saved order ID and no payment attempt.

```powershell
docker compose start menu
```

Wait for listening, then refresh the menu. OrderService recovers without a restart.
Inspect the workflow logs:

```powershell
docker compose logs --tail 100 order payment
```

Find the order ID and its transition from Pending to confirmed payment. JSON log
scopes contain trace IDs for following HTTP activity. Distinguish a persistent
order ID, the stable payment request key used for replay, and an HTTP trace/request
identifier. There is no centralized tracing dashboard or message broker.

Question: “Why does a working OrderService identity endpoint not prove it can accept
a new order?” Expected answer: its local process can be running while an HTTP
dependency is unavailable. Compose `depends_on` does not guarantee runtime health.

### 5. Demonstrate a definite decline

In the same PowerShell terminal:

```powershell
$env:SIMULATE_DECLINE = 'true'
docker compose up -d --no-deps payment
```

Wait for PaymentService, then send request 11 or place a new UI order. Expect
checkout 402, a saved Pending order and a Declined payment. The UI retains the
order number. Request 12 confirms staff cannot force it to Paid (409). In the UI,
check the payment outcome, then cancel the confirmed declined order if desired.
MenuService, OrderService and WebApp were not recreated.

### 6. Demonstrate an ambiguous payment result

```powershell
$env:SIMULATE_DECLINE = 'false'
$env:PAYMENT_DELAY_MS = '7000'
docker compose up -d --no-deps payment
```

With the default five-second OrderService payment timeout, send request 13 once.
PaymentService commits before delaying its response. Checkout returns 503 with
Unknown and a saved order ID. This is not a confirmed decline.

Send request 14: the payment is already Succeeded. Send request 15, or select
Check payment outcome in the UI: the existing payment is validated and Pending
becomes Paid. Its payment ID is unchanged. Repeat reconciliation to show it does
not issue a second payment or regress fulfillment. The dedicated payment suite
also verifies this behavior after an OrderService restart.

Ask: “What if OrderService crashes after payment commits but before saving Paid?”
Answer: the records can disagree until reconciliation. A timeout does not roll
back another service's transaction. There is no atomic cross-service commit.

Restore normal simulation before continuing:

```powershell
$env:SIMULATE_DECLINE = 'false'
$env:PAYMENT_DELAY_MS = '0'
docker compose up -d --no-deps payment
```

### 7. Replace containers, keep history

```powershell
docker compose up -d --force-recreate
```

Wait for listening and retrieve the same menu item, order and payment. They remain
because each service remounts its own volume. Optionally demonstrate `docker
compose down` followed by `docker compose up -d`; omit `--volumes` when retaining
history. Menu seeding happens only when its schema is first created.

Question: “Does Docker make the order/payment update atomic?” Expected answer:
no; containers change packaging and deployment, not the transaction boundary.

### 8. Hand off the student extension

Open [InventoryService assignment](inventory-assignment.md). In
`OrderEndpoints.cs`, locate the comment **Future InventoryService validation belongs
before saving this Pending order**. MenuService continues to own names, prices
and the ordering-availability flag. The proposed new service owns stock counts.
Students add its HTTP client call without reading `inventory.db` from OrderService.

Ask: “Can two customers both pass a stock check when only one item remains?”
Use that question to separate the core validation exercise from the optional
atomic reservation extension. Do not imply the current app reserves inventory.

## Failure quick reference

| Trigger | Expected result | Recovery |
| --- | --- | --- |
| Invalid quantity, missing/unavailable menu item | 400, no saved order/payment | Correct input or restore availability |
| MenuService stopped | Checkout 503, no saved order; existing order reads work | Start menu, refresh; retry only the rejected checkout |
| Simulation decline enabled | Checkout 402, Pending order, Declined payment | Inspect/cancel the declined order; no new attempt is supported for that same order |
| PaymentService stopped | Checkout 503, saved Pending order, Unknown | Start payment, reconcile; missing record is 409 and does not initiate payment |
| Response delay longer than payment timeout | Checkout 503, possibly successful saved payment | Reconcile the retained order ID; never use a new checkout as a status lookup |
| Browser loses checkout response without an ID | Browser pauses checkout; a server-side order may exist | Inspect recent orders before explicitly starting a new cart |
| Same payment request replayed | 200, same payment ID | No second record; changed inputs are 409 |
| Staff tries Pending -> Paid or skips preparation steps | 409 | Confirm payment/reconcile, then follow allowed transitions |
| Container recreated | Same persisted menu/order/payment data | Named volumes are retained; this is not a schema upgrade |

## Source tour and wrap-up

| Concept | File to show |
| --- | --- |
| Single capability and data ownership | `src/MenuService/MenuDbContext.cs`, `src/OrderService/OrderDbContext.cs`, `src/PaymentService/Payment.cs` |
| Consumer-owned contracts and HTTP client factory | `src/OrderService/MenuClient.cs`, `PaymentClient.cs`, `Program.cs` |
| Server pricing and payment workflow | `src/OrderService/OrderEndpoints.cs`, `PaymentWorkflow.cs` |
| Persistent duplicate-payment protection | `src/PaymentService/PaymentEndpoints.cs`, `Payment.cs` |
| Direct browser/API communication and checkout guard | `src/WebApp/wwwroot/app.mjs`, `core.mjs` |
| Independent packaging and storage | Service Dockerfiles and `docker-compose.yml` |

Optional cleanup request 16 removes only the dedicated lecture product. Order and
payment history remain. Keep simulation settings at false/0 for the next session.
If you remove shell overrides later, remember `.env` can supply different values.
Stopping the stack with `docker compose down` retains the databases.

Close with the intentional limits: local demo APIs lack authentication; SQLite is
used as one writer instance per service; schema changes need a deliberate migration
strategy; stock is not reserved; repeated checkout can create distinct orders;
and reconciliation is manual rather than a durable recovery process. Students
should explain these trade-offs rather than mistake the app for a production template.

## Rehearsal checks

Use the read-only preflight for the live demo. Existing isolated suites can be
run before class when more assurance is needed:

```powershell
./scripts/test-menu.ps1
./scripts/test-orders.ps1
./scripts/test-payments.ps1
node --test --test-isolation=none scripts/test-web-core.mjs
./scripts/test-docker.ps1
```

The backend suites use fresh SQLite databases; the Docker suite uses its own
uniquely named disposable project. They do not reset the live classroom stack.
See each phase walkthrough for ports, prerequisites, exact scope and recorded
verification. Node is optional for the frontend logic tests, not required to run
the app. No further application feature is needed to begin the student assignment.

Phase 7 verification: the read-only preflight passed against the running stack,
including seven available products and matching browser CORS/configuration.
Local documentation links, all 10 JSON demonstration payloads and five named
REST Client response references were checked. Application behavior is supported
by the isolated suites recorded above; the classroom mutations were not replayed
against the live demo data during this documentation phase.
