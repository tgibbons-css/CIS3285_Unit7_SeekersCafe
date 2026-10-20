using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PaymentService;

var builder = WebApplication.CreateBuilder(args);
// Send structured logs to the console, including scopes for additional diagnostic context.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
// Return a client error for invalid route or JSON binding instead of throwing an exception.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

// PaymentService owns its SQLite database. Fail at startup if its connection string is
// missing, and resolve a relative file path from this service's content root.
var connection = new SqliteConnectionStringBuilder(
    builder.Configuration.GetConnectionString("PaymentDatabase")
    ?? throw new InvalidOperationException("PaymentDatabase connection string is required."));
connection.DataSource = Path.GetFullPath(connection.DataSource, builder.Environment.ContentRootPath);
builder.Services.AddDbContext<PaymentDbContext>(options => options.UseSqlite(connection.ToString()));

// This demo can delay payment responses to simulate a slow dependency. Validate the
// configured range on startup so accidental long waits or negative delays are rejected.
var delay = builder.Configuration.GetValue<int>("Simulation:ResponseDelayMilliseconds");
if (delay is < 0 or > 10000)
    throw new InvalidOperationException("Simulation:ResponseDelayMilliseconds must be from 0 to 10000.");

// Return consistent problem details for errors and log request metadata without logging
// sensitive payment request or response bodies.
builder.Services.AddProblemDetails();
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath
        | HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration;
    options.CombineLogs = true;
});

var app = builder.Build();
// Log incoming requests, turn unhandled exceptions into problem responses, and provide
// responses for otherwise-empty status codes before requests reach the API endpoints.
app.UseHttpLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

// Create the database schema when the service first starts. EnsureCreated is suitable for
// this demo's fresh database, but does not upgrade an existing schema.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// The root route identifies this service. Payment endpoints receive the simulation settings
// so the demo can optionally delay responses or decline payment attempts.
app.MapGet("/", () => Results.Ok(new { service = "PaymentService", phase = 4 }));
app.MapPaymentEndpoints(delay, builder.Configuration.GetValue<bool>("Simulation:DeclinePayments"));
app.Run();
