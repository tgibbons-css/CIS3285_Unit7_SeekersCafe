# Docker and Compose

Each app has an independent Linux image. Compose connects them over HTTP and mounts
one persistent SQLite volume per business service. WebApp owns no database.
These containers run the same APIs and frontend as local .NET development.

## Start the application

Use Docker Desktop with the Linux engine running. Docker Compose v2 is required;
the tested versions are Engine 28.5.2 and Compose 2.40.3. The host does not need a
.NET SDK for this workflow, because compilation happens in the SDK build images.
The first build needs access to Microsoft Container Registry and NuGet.

Stop any `dotnet run` copies using ports 5100–5103. From the solution directory:

```powershell
docker version
docker compose config --quiet
docker compose up --build -d
docker compose ps
docker compose logs --tail 30
```

Open **http://localhost:5100**. The first startup creates the databases and seeds
the menu. A container marked Running may still be initializing; wait for the logs'
`Now listening on` message, or check the API:

```powershell
Invoke-RestMethod http://localhost:5101/api/menu | Format-Table
Invoke-RestMethod http://localhost:5100/health
```

This intentionally small Compose setup has no health-check utility or automatic
readiness gate. `depends_on` creates menu/payment before order, but does not wait
for application readiness and does not stop dependent services after an outage.
The integration test polls HTTP readiness explicitly. During startup, use Refresh
menu if the page loads before an API is ready. At runtime the existing service
timeouts/error handling still apply. Containers are not automatically restarted,
so deliberate classroom failures remain visible.

## Network addresses

| App | Browser/REST Client URL | Container address | Database volume |
| --- | --- | --- | --- |
| MenuService | `http://localhost:5101` | `http://menu:8080` | `menu-data` at `/data/menu.db` |
| OrderService | `http://localhost:5102` | `http://order:8080` | `order-data` at `/data/orders.db` |
| PaymentService | `http://localhost:5103` | `http://payment:8080` | `payment-data` at `/data/payments.db` |
| WebApp | `http://localhost:5100` | `http://web:8080` | None |

All containers listen on port 8080 in their own network namespaces. Host port
mappings distinguish them. Published ports bind only the host's loopback interface
for this local classroom application. Use the `localhost` browser URLs shown above.

OrderService uses Compose DNS names for MenuService and PaymentService. `localhost`
inside its container would refer to OrderService itself. The browser runs outside
Docker and cannot resolve `menu` or `order`, so WebApp's `/api/config` returns host
URLs. There is no API gateway and no direct browser-to-PaymentService call.

## Image and configuration design

Each `src/<App>/Dockerfile` uses two stages:

1. `mcr.microsoft.com/dotnet/sdk:10.0.402-noble` restores and publishes that project
   in Release configuration. The project file is copied before source files so
   ordinary source edits can reuse the restore layer. The SDK image supplies a
   .NET 10 SDK; `global.json` selects the latest installed .NET 10 feature band.
2. `mcr.microsoft.com/dotnet/aspnet:10.0.13-noble` contains only the runtime and
   published output. `UseAppHost=false` produces a framework-dependent DLL launched
   with `dotnet`; no Windows executable or SDK is needed in the runtime image.

The Docker image version tags are explicit. When intentionally changing the .NET
version used to build the containers, update the Docker SDK tag and check the runtime
image and EF Core versions together. Local development can use any stable .NET 10 SDK
installed on the host. These images are built locally, not pushed to a registry.

All apps run as the built-in non-root `app` user (`APP_UID`, currently 1654).
The three database images create `/data` and give it to that user. A new empty
named volume inherits the directory's ownership, allowing SQLite to create the
database, journal and WAL files there. No service mounts another's data. WebApp
has no `/data` directory or volume. Do not scale multiple writer containers against
one SQLite file in this lesson.

`ASPNETCORE_ENVIRONMENT=Docker` selects each project's `appsettings.Docker.json`.
This is a custom environment name, not Development. Local `appsettings.json` and
launch profiles remain usable. Published containers do not use launch profiles.

| Setting | Local default | Docker override |
| --- | --- | --- |
| Business DB connection | Project-relative `*.db` | Absolute `/data/*.db` |
| OrderService menu URL | `localhost:5101` | `menu:8080` |
| OrderService payment URL | `localhost:5103` | `payment:8080` |
| Browser-facing URLs | `localhost:5101/5102` | Host ports selected by Compose |
| Browser CORS origin | `http://localhost:5100` | `Frontend__Origin`, matching configured WebApp host port |

Base settings still supply HTTP timeouts and logging. Compose environment values
override JSON settings. `.dockerignore` keeps Git data, local SQLite files, tool
caches, host build output and documentation out of the build context. No existing
project-local databases are copied into an image or imported into Docker volumes.

Optional configuration:

```powershell
Copy-Item .env.example .env
```

Edit the copy to change host ports or simulation flags, then run `docker compose
up -d` to recreate services whose configuration changed. `.env` is ignored by Git.
Shell environment variables take precedence over `.env`; defaults are in the
Compose file. Changing `WEB_PORT` also changes the one allowed browser CORS origin.
The domain remains `localhost`. Keep a stable Compose project name/directory when
you want to reuse the same volumes.

## Stop and start individual services

```powershell
docker compose stop menu
docker compose ps -a
```

Existing orders remain readable, PaymentService stays up, and the frontend remains
available. A new checkout cannot validate products and returns a clear menu error.
Restart only MenuService:

```powershell
docker compose start menu
```

Refresh the menu or retry a checkout that was explicitly rejected before an order
was saved. A payment failure after a Pending order was saved requires a different
response: keep its ID and use Check payment outcome, not a new checkout POST.

```powershell
docker compose stop payment
docker compose start payment
docker compose logs --tail 40 order payment
```

For a payment outage, OrderService preserves Pending and reports Unknown. After
restart, reconciliation can find a saved result, or report that no record exists.
It never initiates another payment. See [payment demonstrations](payment-service.md).

Rebuild just one service after a source change:

```powershell
docker compose up --build -d --no-deps menu
```

The other containers and all database volumes remain in place. To demonstrate
independent startup without automatically starting dependencies:

```powershell
docker compose up -d --no-deps order
```

OrderService can start and read its database while menu/payment are unavailable;
new checkout still needs both HTTP dependencies. `up order` without `--no-deps`
also starts its declared dependencies.

## Decline and late-response demonstrations

Change PaymentService's simulation configuration without restarting the other apps:

```powershell
$env:SIMULATE_DECLINE = 'true'
docker compose up -d --no-deps payment
```

Place a new order: 402, Pending and confirmed decline. Existing payment results
remain immutable. Restore normal payments or demonstrate a payment committed
before its response arrives:

```powershell
$env:SIMULATE_DECLINE = 'false'
$env:PAYMENT_DELAY_MS = '7000'
docker compose up -d --no-deps payment
```

With OrderService's default five-second payment timeout, checkout reports Unknown
even though PaymentService saved Succeeded. Use Check payment outcome in the page
to observe Paid with the same payment ID. Then reset the configuration:

```powershell
Remove-Item Env:SIMULATE_DECLINE -ErrorAction SilentlyContinue
Remove-Item Env:PAYMENT_DELAY_MS -ErrorAction SilentlyContinue
docker compose up -d --no-deps payment
```

If `.env` contains simulation overrides, reset those too. `docker compose restart`
does not apply changed environment values; use `up -d` to recreate the container.

## Persistent data and cleanup

```powershell
docker compose down
docker compose up -d
```

`down` removes this project's containers and network but retains its named volumes.
Menu edits, order history and payment records survive. `up --force-recreate -d`
also retains those volumes. Each service still uses `EnsureCreatedAsync`; an
existing menu is not reseeded on restart. Docker does not add schema migrations.

Inspect storage and process ownership:

```powershell
docker volume ls
docker compose exec menu id
docker compose exec menu ls -l /data
docker compose exec order ls -l /data
docker compose exec payment ls -l /data
```

Compose prefixes volume names with its project name, e.g.
`cis3285_unit7_seekerscafe_menu-data`. Project-local `.db` files used by `dotnet run`
are separate from this data in Docker Desktop's Linux storage.

Only when intentionally resetting disposable container demo data:

```powershell
# Destructive: removes this Compose project's three databases, including history.
docker compose down --volumes
```

Reset all three together. Retaining payments while resetting only orders can cause
numeric order-ID collisions and rejected payments. No cleanup step should target
unrelated Docker projects or prune the user's whole Docker installation.

## Repeatable integration checks

```powershell
./scripts/test-docker.ps1
```

The script builds images, assigns a unique `seekers-qa-...` project name and uses
ports 5590–5593 by default. It creates fresh test-only volumes and never touches
the normal Compose project or local .NET demo databases. Logs are retained under
`.local/docker-tests/<project>/`. The final cleanup removes only its disposable
containers, volumes, network and image tags. Official base images/build cache can
remain available for subsequent builds. Override script port parameters if needed.

Checks cover all four containers, non-root execution, exclusive database mounts,
published frontend assets, browser URLs and CORS, successful checkout over Docker
DNS, persistence through forced recreation and down/up, individual dependency
outages, payment-only configuration changes, declines, late payment responses,
reconciliation and independent OrderService/WebApp startup.

Phase 6 verification built all four images and passed 26 HTTP checks plus
non-root ownership, exclusive-volume, DNS, asset, CORS and persistence assertions
on the Linux Docker engine. Test containers and their disposable volumes were
removed afterward. Existing project-local databases and other Compose projects
were not modified.

## Troubleshooting

- If `docker version` shows only Client or a missing Linux-engine pipe, start
  Docker Desktop and wait for its Linux engine. The CLI being installed is not
  enough to build or run containers.
- For a port conflict, stop the corresponding `dotnet run` process or select
  different host ports in `.env`. Inspect `docker compose config` to see resolved
  browser URLs, CORS origin and mappings.
- A custom existing volume with different ownership may prevent SQLite writes.
  Inspect ownership and logs; do not delete retained data merely to clear an error.
- Base-image or NuGet downloads require network access on the Docker host/builder.
- If the UI loads but APIs fail, verify browser URLs use host ports and OrderService
  uses Compose service names. Refresh menu after startup and inspect service logs.

References: [Microsoft ASP.NET Core container tutorial](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/docker/building-net-docker-images?view=aspnetcore-10.0),
[Compose networking](https://docs.docker.com/compose/how-tos/networking/), and
[Compose startup behavior](https://docs.docker.com/compose/how-tos/startup-order/).
