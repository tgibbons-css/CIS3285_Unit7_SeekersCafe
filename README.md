# Seekers Café — Online Ordering Demonstration

An educational simulation for CIS 3285: Advanced Software Design at the College
of St. Scholastica. This is not an official campus or dining services application.
Products will be illustrative; payments will be simulated without real credentials.

## Current milestone: Phase 4

MenuService now implements validated CRUD with EF Core SQLite and seven sample
products seeded on database creation. OrderService persists orders, retrieves menu
data through HTTP and calculates totals on the server. PaymentService simulates
Credit Card and Dining Dollars payments with persistent duplicate protection.
Confirmed payments mark orders Paid; declines and uncertain outcomes retain Pending.
Reconciliation checks a saved payment without charging again. Only WebApp remains
a project shell. Frontend assets and Docker are deferred to later phases.
InventoryService remains a future student assignment.
See the [MenuService walkthrough](docs/menu-service.md) and
[OrderService walkthrough](docs/order-service.md) and
[Phase 4 payment demonstrations](docs/payment-service.md).

| Project | Local URL | Responsibility | Owned database |
| --- | --- | --- | --- |
| MenuService | http://localhost:5101 | Menu CRUD and availability | menu.db |
| OrderService | http://localhost:5102 | Validate menu items, price and manage orders | orders.db |
| PaymentService | http://localhost:5103 | Simulated, deduplicated payments | payments.db |
| WebApp | http://localhost:5100 | Serve plain HTML, CSS, and JavaScript | None |

## Build and run

Requires the .NET 10 SDK (initially 10.0.402; `global.json` allows newer patches
within the same SDK feature band). The three business services use EF Core SQLite 10.0.13
NuGet package. The first restore requires internet access to NuGet.

From this directory:

```powershell
dotnet --version
dotnet restore SeekersCafe.sln
dotnet build SeekersCafe.sln --no-restore
```

Run each project in a separate VS Code terminal:

```powershell
dotnet run --project src/MenuService --launch-profile http
dotnet run --project src/OrderService --launch-profile http
dotnet run --project src/PaymentService --launch-profile http
dotnet run --project src/WebApp --launch-profile http
```

Visit each URL above or use `requests/phase1.http`, `requests/menu.http` and
`requests/orders.http` and `requests/payments.http` with the VS Code REST Client
extension. A PowerShell alternative is:

```powershell
Invoke-RestMethod http://localhost:5101/
Invoke-RestMethod http://localhost:5101/api/menu
```

Stop a process with Ctrl+C. Stop MenuService and confirm the other three roots
still respond: the projects neither reference nor start one another.
Local ports come from each project's `Properties/launchSettings.json`. These
profiles are local tooling settings, not deployment configuration.

In an environment that restricts writes to the user profile, set these before
running the build commands (ordinary local development does not require them):

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.local/dotnet'
$env:NUGET_PACKAGES = Join-Path $PWD '.local/nuget/packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
```

Verification in the restricted workspace used single-process MSBuild and disabled
the shared compiler server:

```powershell
dotnet restore SeekersCafe.sln -m:1
dotnet build SeekersCafe.sln --no-restore -m:1 -p:UseSharedCompilation=false
```

During Phase 1, all four application roots returned HTTP 200 with the expected identity. After
stopping MenuService, the other three roots remained reachable. The build completed
with zero warnings and errors. This checks the project shells, not future business
workflow fault handling. Verification processes were stopped afterward.

## Setup commands used

```powershell
dotnet new sln --name SeekersCafe --format sln
dotnet new web --name MenuService --output src/MenuService --framework net10.0 --no-restore
dotnet new web --name OrderService --output src/OrderService --framework net10.0 --no-restore
dotnet new web --name PaymentService --output src/PaymentService --framework net10.0 --no-restore
dotnet new web --name WebApp --output src/WebApp --framework net10.0 --no-restore
dotnet sln SeekersCafe.sln add src/MenuService/MenuService.csproj src/OrderService/OrderService.csproj src/PaymentService/PaymentService.csproj src/WebApp/WebApp.csproj
```

The empty `web` template avoids unrelated example endpoints and dependencies.
WebApp will use ASP.NET Core only to serve static files; no UI framework is planned.

## Incremental development

1. **Solution setup — complete:** project shells, fixed ports, architecture, HTTP smoke requests.
2. **MenuService — complete:** EF Core SQLite, initial schema and sample seed, validated CRUD, independent HTTP tests.
3. **OrderService — complete:** order persistence, menu HTTP client, server-calculated prices and dependency failure tests.
4. **PaymentService — complete:** simulated success/decline, unique payment request key, integration, reconciliation and timeout behavior.
5. **Web interface:** browse, cart, checkout, confirmation and status lookup.
6. **Docker:** per-project Dockerfiles, Compose DNS/configuration, separate persistent database volumes.
7. **Classroom demonstration:** walkthrough, failures and InventoryService assignment guidance.

Proceed only when the instructor requests the next phase.
See [architecture and design decisions](docs/architecture.md).
