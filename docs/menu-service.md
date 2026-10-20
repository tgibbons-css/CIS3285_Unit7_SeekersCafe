# Phase 2: MenuService

MenuService can now run entirely on its own. It creates `src/MenuService/menu.db`
when launched with `dotnet run --project src/MenuService --launch-profile http`.
No other service needs to be running, and no other project references its classes
or receives its connection string.

## Run and explore

```powershell
dotnet restore SeekersCafe.sln
dotnet build SeekersCafe.sln --no-restore
dotnet run --project src/MenuService --launch-profile http
```

In another terminal:

```powershell
Invoke-RestMethod http://localhost:5101/api/menu | Format-Table
```

The initial menu contains Latte ($4.50), Cappuccino ($4.25), Drip Coffee ($2.50),
Hot Chocolate ($3.50), Blueberry Muffin ($3.00), Breakfast Sandwich ($5.75), and
Bagel ($2.75). These are illustrative products and prices, not a verified campus menu.

Open `requests/menu.http` in VS Code with REST Client for a create/update/delete
walkthrough and invalid requests. Send its named `createItem` request before its
update and delete requests; they use the ID from the create response.

## API contract

All responses use camelCase JSON. Item responses contain `menuItemId`, `name`,
`description`, `category`, `price` (decimal dollars) and `isAvailable`.

| Request | Behavior | Success |
| --- | --- | --- |
| `GET /api/menu` | Available items, ordered by category then name | 200, array |
| `GET /api/menu?includeUnavailable=true` | All items, including unavailable ones | 200, array |
| `GET /api/menu/{id}` | One item, even if unavailable | 200, item |
| `POST /api/menu` | Create a validated item with a generated ID | 201, item and Location header |
| `PUT /api/menu/{id}` | Replace all five editable fields | 200, updated item |
| `DELETE /api/menu/{id}` | Permanently remove the item | 204, no body |

Unknown IDs return 404. Invalid field values return 400 with Problem Details and
an `errors` dictionary. Malformed JSON or incompatible types return 400 before the
handler executes. An empty menu returns `[]`. PUT never creates a missing item.

POST and PUT take the same body:

```json
{
  "name": "Demo Tea",
  "description": "Sample product",
  "category": "Other Drinks",
  "price": 2.95,
  "isAvailable": true
}
```

All five fields are required. Name and category must be nonblank and at most 100
and 50 trimmed characters respectively. Description may be empty and is limited to
500 characters. Strings are trimmed before storage. Price must be from $0 to
$1,000 with at most two decimal places; extra fractional cents are rejected rather
than rounded. Availability must be an explicit JSON boolean. A separate request
type prevents overposting the database ID. Unknown JSON properties are ignored.

For future checkout, looking up an unavailable product returns its details with
`isAvailable: false`; OrderService must check that flag. This is availability,
not a stock count or reservation. No InventoryService exists yet.

## Files and ASP.NET Core conventions

- `Program.cs` registers the scoped EF Core DbContext with dependency injection,
  resolves the database location, initializes the schema/seed and maps the endpoints.
- `MenuDbContext.cs` owns persistence mapping. The price value converter stores
  exact integer cents in `PriceCents` while C# and JSON expose decimal dollars.
- `MenuItem.cs` is the persistent entity; `MenuItemRequest.cs` defines and validates
  editable input without exposing an assignable ID.
- `MenuEndpoints.cs` groups Minimal API handlers under `/api/menu`. ASP.NET Core
  supplies the scoped DbContext and request cancellation token. Reads use
  `AsNoTracking`; writes use EF change tracking and asynchronous `SaveChangesAsync`.
- `MenuSeed.cs` contains only the seven illustrative sample products.

There is no repository layer or shared business-model project. Database access is
visible in the handlers because the business capability is small. Other services
will consume the JSON contract through HTTP rather than reuse this DbContext.

Structured JSON console logging records HTTP method, path, status and duration
without bodies or credentials. Using only the console provider avoids requiring
Windows Event Log write permissions and works the same way in future containers.
CRUD logs include the menu item ID. Expected validation and missing-item cases use
400/404; unexpected server errors use the exception handler with Problem Details.
Local browser CORS allows only `http://localhost:5100`. CORS is not authentication;
these mutation endpoints are intended for a local classroom demo.

## Database lifecycle and configuration

`ConnectionStrings:MenuDatabase` defaults to `Data Source=menu.db`. Relative paths
resolve against the application's content root, which `dotnet run --project`
sets to the project directory. For a deployed DLL, explicitly set its content root
or configure an absolute database path. The parent directory must already exist
and be writable. An environment override is supported:

```powershell
$env:ConnectionStrings__MenuDatabase = 'Data Source=C:/existing-directory/menu.db'
dotnet run --project src/MenuService --launch-profile http
Remove-Item Env:ConnectionStrings__MenuDatabase
```

`EnsureCreatedAsync` creates a missing schema; sample products are inserted only
when that call creates the schema. Restarting retains edits, deletes and an
intentionally emptied menu. Startup uses one local service instance; there is no
multi-instance initializer coordination. Schema creation and seeding are separate
steps, so interruption between them can leave an empty initialized database.
This classroom simplification can be revisited with migrations and coordinated
seeding when deployment requirements grow.

`EnsureCreatedAsync` does not upgrade an existing schema. To reset disposable demo
data, stop MenuService and delete its `menu.db` and any matching SQLite sidecar
files, then restart. Do not delete a database whose data you need. Introduce EF
migrations deliberately before preserving data across schema changes; do not mix
`EnsureCreated` with migrations. Database files are ignored by Git.

## Independent integration checks

Build first, then run:

```powershell
./scripts/test-menu.ps1
# Choose a different test port if 5191 is occupied:
./scripts/test-menu.ps1 -Port 5192
```

The script starts only MenuService against a new real SQLite database under
`.local/menu-tests/<unique-run>/menu.db`. It tests seed data, full CRUD, exact price
round-trips, availability filters, invalid input, malformed JSON, missing IDs,
CORS, persistence across restarts and an emptied menu staying empty. Its database
and logs remain in the ignored test directory for inspection. It stops its own
service process in a `finally` block and never modifies the normal classroom menu.

Phase 2 verification: the solution built with zero warnings and errors, and the
script passed all 44 HTTP checks in both Development and Production. The normal Development launch on port 5101 also
returned all seven products and created the default project-local `menu.db`.
Request logging was verified to include GET, `/api/menu`, status 200 and duration.
