# The café web interface

WebApp now serves plain HTML, CSS and JavaScript from `wwwroot`. There is no UI
framework, bundler, frontend package installation or shared business-model library.
The page uses a campus café palette, clear sample/simulation labeling, and a
responsive menu/cart layout. Its coffee illustration is drawn in CSS and needs
no external assets or network fonts.

## Start and use

Build from the solution directory:

```powershell
dotnet restore SeekersCafe.sln
dotnet build SeekersCafe.sln --no-restore
```

Run each command in its own terminal:

```powershell
dotnet run --project src/MenuService --launch-profile http
dotnet run --project src/OrderService --launch-profile http
dotnet run --project src/PaymentService --launch-profile http
dotnet run --project src/WebApp --launch-profile http
```

Open **http://localhost:5100**. Use `localhost`, not `127.0.0.1`, and port 5100:
the API CORS policies allow this exact frontend origin. Stop each service with
Ctrl+C after the demonstration. The browser does not start backend processes.

1. Browse categories or search product names/descriptions. Refresh menu retrieves
   current availability and prices. Loading, empty, no-match and service-error
   states provide explicit feedback.
2. Add items, increase/decrease quantities, or remove a line. Quantities are
   limited to 20 per item. The estimated total uses integer cent arithmetic.
3. Enter a customer name and select simulated Credit Card or Dining Dollars.
   The page never requests card numbers or student account information.
4. Place the simulated order. Checkout is locked while submitting. The browser
   sends only name, method, menu IDs and quantities; OrderService calculates the
   confirmed prices and total.
5. Read the confirmation and saved order number. The order detail uses server
   snapshots and the confirmed total, which can differ from the cart estimate
   if a menu price changed between browsing and checkout.
6. Track any known order number and use Refresh status for manual updates.
7. Open **Behind the counter · Staff demo**, load orders, and advance a paid
   order through Preparing, Ready and Completed. Refresh the customer status
   afterward. Staff and customer views use the same OrderService APIs; there is
   no authentication or production role separation in this classroom demo.

## Declines and uncertainty

The page understands Phase 4's separate checkout outcomes:

| Backend result | Browser behavior |
| --- | --- |
| 201, paid order | Shows order number and confirmed total; clears the submitted cart |
| 400 or documented pre-creation menu failure | Preserves the cart, explains rejection, permits a corrected submission |
| 402, saved order with declined payment | Retains order number, shows Pending and decline message; clears submitted cart |
| Payment-related 503 with saved order ID | Retains order number, explains unknown outcome; offers Check payment outcome |
| Broken connection or unrecognized response with no order ID | Preserves cart and pauses checkout, because the server might have committed an order |

**Check payment outcome** calls OrderService's reconciliation endpoint. It never
creates a payment. The page shows a confirmed result, or the server's explanation
that the outcome is still unknown/no record is available. A Pending order also
offers **Cancel declined order**; the server permits cancellation only when a
decline is confirmed. Staff cannot force Pending to Paid.

If no order ID came back, inspect recent orders in the staff demo before selecting
**I checked my orders; start a new cart**. This explicit acknowledgment empties the
cart and removes the pause; it does not delete or cancel any existing order.
No background checkout retry is performed. Client-side guarding reduces accidental
duplicates but is not server-side submission idempotency: opening another tab or
deliberately submitting a new order can still create duplicates.

The cart, last tracked order ID and in-flight/uncertain checkout guard are kept in
`sessionStorage` for the current tab. The customer name is not saved there. A page
refresh restores the cart and guard, and can refresh the last tracked order.
When browser storage is disabled, the in-memory guard still works for the current
page but cannot survive a reload. A refresh retrieves current menu prices before
checkout is enabled. Items removed from the menu remain visible in the cart with
a removal instruction instead of silently disappearing.

## Communication and files

- `Program.cs` serves default/static files and read-only `/api/config` and `/health`.
- `wwwroot/index.html` contains semantic landmarks, labeled forms, live status
  regions, a skip link and the menu/cart/tracking/staff sections.
- `wwwroot/styles.css` contains the café theme and small-screen layout. The menu
  and cart stack on narrow screens. Focus outlines and reduced-motion preferences
  are supported.
- `wwwroot/app.mjs` manages DOM rendering, cart state, HTTP calls and UI feedback.
  API-supplied names/descriptions are inserted with `textContent`, not as HTML.
  Dynamic buttons restore keyboard focus after cart/menu updates.
- `wwwroot/core.mjs` defines small pure functions for currency totals, validated
  checkout input and classifying responses without losing an uncertain order.

The browser reads these public settings from `/api/config`:

```json
{
  "menuBaseUrl": "http://localhost:5101",
  "orderBaseUrl": "http://localhost:5102"
}
```

WebApp's `Services:MenuBaseUrl` and `Services:OrderBaseUrl` configuration values can
be overridden by `Services__MenuBaseUrl` and `Services__OrderBaseUrl`. URLs must be
absolute HTTP(S); only their origin is used, since API routes are at the host root.
They must be reachable from the browser. There are no secrets in this configuration.
PaymentService is called only by OrderService. WebApp owns no business database
and provides no checkout proxy or gateway. Browser requests have a 15-second
deadline; timeout during checkout is treated conservatively as uncertain.

## Testing

The .NET build validates the host. Node.js is optional for running the small
frontend logic checks; it is not a runtime or build dependency for the app:

```powershell
node --check src/WebApp/wwwroot/app.mjs
node --test --test-isolation=none scripts/test-web-core.mjs
```

The six tests cover exact cart totals, server-controlled pricing contracts,
quantity/name/method validation, and distinguishing saved, rejected and uncertain
checkout results. The isolation flag lets the tests run in restricted environments
without spawning a test worker.

For an isolated browser demonstration, after building Debug configuration:

```powershell
./scripts/start-web-preview.ps1
```

This test helper launches the four apps with fresh SQLite databases under
`.local/web-tests/<run>/`, verifies frontend assets/configuration and CORS, then
prints the preview URL and an exact cleanup command. It uses frontend port 5100
and backend ports 5491–5493, leaving normal demo databases untouched. It fails if
a required port is occupied. `-DeclinePayments` or
`-PaymentDelayMilliseconds 3500` can demonstrate failures (the isolated order
host uses a two-second payment timeout). Do not run two previews on the same ports.
Stop it afterward with the printed command:

```powershell
./scripts/stop-web-preview.ps1 -ManifestPath '<printed path to processes.json>'
```

The cleanup checks saved process IDs, start times and executable names before
stopping the owned processes. Test databases/logs remain for inspection.

Phase 5 verification used the real APIs and isolated SQLite databases in Chrome:
search/filtering and a $12.00 cart; keyboard and mouse checkout; paid confirmation;
staff advancement; declined checkout, payment checking and cancellation; a
post-commit payment timeout followed by successful reconciliation; loss of
OrderService with checkout guard retained across reload; missing-order feedback;
and MenuService outage/recovery. The mobile layout was checked at 390 pixels with
no horizontal page overflow. A screenshot is saved in `docs/images/web-interface.jpg`.

## Intentional limits

Order updates are manually refreshed. Staff lists are unpaged and available to
anyone who can reach the local demo. This page is not an official campus system.
It does not authorize real payments, reserve stock, retry declined payments,
recover every ambiguous submission, or implement InventoryService. Containers are
now available through the [Phase 6 Docker setup](docker.md). The final classroom
demonstration material remains Phase 7.
