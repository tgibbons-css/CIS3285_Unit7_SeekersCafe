# Seekers Café — Online Ordering Demonstration

An educational simulation for CIS 3285: Advanced Software Design at the College
of St. Scholastica. This is not an official campus or dining services application.
Products will be illustrative; payments will be simulated without real credentials.

## Initial application complete — Phases 1–7

Start with the [30-minute classroom walkthrough](docs/classroom-demo.md). The
[InventoryService assignment](docs/inventory-assignment.md) hands the next
business capability to students; it has no implementation in this baseline.

MenuService now implements validated CRUD with EF Core SQLite and seven sample
products seeded on database creation. OrderService persists orders, retrieves menu
data through HTTP and calculates totals on the server. PaymentService simulates
Credit Card and Dining Dollars payments with persistent duplicate protection.
Confirmed payments mark orders Paid; declines and uncertain outcomes retain Pending.
Reconciliation checks a saved payment without charging again. WebApp now serves a
responsive café interface for browsing, cart editing, simulated checkout, order
tracking and a small classroom staff queue. Each app now has a Docker image;
Compose coordinates networking and three separate persistent SQLite volumes.
InventoryService remains a future student assignment.
See the [MenuService walkthrough](docs/menu-service.md) and
[OrderService walkthrough](docs/order-service.md) and
[Phase 4 payment demonstrations](docs/payment-service.md), plus the
[Phase 5 web interface walkthrough](docs/web-interface.md) and
[Phase 6 Docker walkthrough](docs/docker.md).

Before a presentation, with the four apps running:

```powershell
./scripts/check-demo.ps1
```

This read-only preflight checks service availability, available menu data, browser
configuration and CORS. Use [classroom.http](requests/classroom.http) for the
guided server-pricing, snapshot, replay and failure demonstrations.

| Project | Local URL | Responsibility | Owned database |
| --- | --- | --- | --- |
| MenuService | http://localhost:5101 | Menu CRUD and availability | menu.db |
| OrderService | http://localhost:5102 | Validate menu items, price and manage orders | orders.db |
| PaymentService | http://localhost:5103 | Simulated, deduplicated payments | payments.db |
| WebApp | http://localhost:5100 | Serve plain HTML, CSS, and JavaScript | None |

## Build and run

For the container version, start Docker Desktop in Linux-container mode, then run:

```powershell
docker compose up --build -d
docker compose ps
```

Open **http://localhost:5100** once the APIs are listening. Stop any locally running
copies first so ports 5100–5103 are free. `docker compose down` stops/removes the
containers and network while retaining the databases. The first image build needs
internet access; a host .NET SDK is not required for the container path. See the
[Docker walkthrough](docs/docker.md) for persistence, per-service commands and tests.

### Run without Docker

Requires any stable .NET 10 SDK. `global.json` uses the 10.0.100 SDK as a minimum
and rolls forward to the latest installed .NET 10 feature band, so students do not
need to install the exact SDK patch used by the project author. The three business
services use the EF Core SQLite 10.0.13 NuGet package. The first restore requires
internet access to NuGet.

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

Open **http://localhost:5100** to use the café interface. Use this exact local
origin: the APIs' browser CORS policy allows `http://localhost:5100`.

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
WebApp uses ASP.NET Core to serve static files and public API configuration;
it has no UI framework or frontend package installation.

## Incremental development

1. **Solution setup — complete:** project shells, fixed ports, architecture, HTTP smoke requests.
2. **MenuService — complete:** EF Core SQLite, initial schema and sample seed, validated CRUD, independent HTTP tests.
3. **OrderService — complete:** order persistence, menu HTTP client, server-calculated prices and dependency failure tests.
4. **PaymentService — complete:** simulated success/decline, unique payment request key, integration, reconciliation and timeout behavior.
5. **Web interface — complete:** browse, cart, checkout, confirmation, status lookup and classroom staff queue.
6. **Docker — complete:** per-project Dockerfiles, Compose DNS/configuration, separate persistent database volumes and container integration checks.
7. **Classroom demonstration — complete:** timed instructor walkthrough, failure quick reference, read-only preflight, named HTTP requests and InventoryService assignment handoff.

See [architecture and design decisions](docs/architecture.md) for the completed
system and its intentional limits. InventoryService remains student work.

## Verification reference

These are the checks recorded during implementation; they are not automatically
rerun by the read-only classroom preflight. See the phase walkthroughs for scope
and prerequisites. All mutation suites use their own disposable data.

| Check | Command | Recorded result |
| --- | --- | --- |
| MenuService CRUD and persistence | `./scripts/test-menu.ps1` | 44 HTTP checks |
| OrderService integration and failures | `./scripts/test-orders.ps1` | 55 HTTP checks |
| Payment simulation, replay and reconciliation | `./scripts/test-payments.ps1` | 73 HTTP checks |
| Frontend cart and response classification | `node --test --test-isolation=none scripts/test-web-core.mjs` | 6 tests |
| Real containers, volumes and HTTP integration | `./scripts/test-docker.ps1` | 26 HTTP checks plus container assertions |
| Live classroom readiness (read-only) | `./scripts/check-demo.ps1` | Checks the currently running stack |

The Docker application can run without a host .NET SDK. Local backend test scripts
need the Debug .NET build; Node is needed only for the optional frontend logic tests.
