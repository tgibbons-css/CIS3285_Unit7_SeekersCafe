using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OrderService;

var builder = WebApplication.CreateBuilder(args);
// Write structured JSON logs to the console so they are easy to read and collect.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
// Treat invalid route/JSON binding as a client error (400), rather than an exception.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
// Serialize order statuses as readable strings and reject numeric enum values in JSON.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<OrderStatus>(allowIntegerValues: false)));

// OrderService owns and reads/writes this SQLite database. It does not access MenuService's
// database; menu information is requested from MenuService through the HTTP client below.
var connection = new SqliteConnectionStringBuilder(
    builder.Configuration.GetConnectionString("OrderDatabase")
    ?? throw new InvalidOperationException("OrderDatabase connection string is required."));
// Resolve relative database paths from this service's content root, not the shell's directory.
connection.DataSource = Path.GetFullPath(connection.DataSource, builder.Environment.ContentRootPath);
builder.Services.AddDbContext<OrderDbContext>(options => options.UseSqlite(connection.ToString()));

// Read and validate the MenuService address at startup so a missing or unusable URL fails
// immediately instead of waiting until a customer submits an order.
var menuUrl = builder.Configuration["MenuService:BaseUrl"];
if (!Uri.TryCreate(menuUrl, UriKind.Absolute, out var menuUri)
    || (menuUri.Scheme != "http" && menuUri.Scheme != "https"))
    throw new InvalidOperationException("MenuService:BaseUrl must be an absolute HTTP(S) URL.");
// Bound the wait for a menu response; the typed client is injected wherever menu access is needed.
var timeout = builder.Configuration.GetValue<int>("MenuService:TimeoutSeconds");
if (timeout is < 1 or > 30)
    throw new InvalidOperationException("MenuService:TimeoutSeconds must be between 1 and 30.");
builder.Services.AddHttpClient<MenuClient>(client =>
{
    client.BaseAddress = menuUri;
    client.Timeout = TimeSpan.FromSeconds(timeout);
});

// Configure a separate typed client for payment processing. Keeping the base address and
// timeout in configuration lets each service be moved without changing application code.
var paymentUrl = builder.Configuration["PaymentService:BaseUrl"];
if (!Uri.TryCreate(paymentUrl, UriKind.Absolute, out var paymentUri)
    || (paymentUri.Scheme != "http" && paymentUri.Scheme != "https"))
    throw new InvalidOperationException("PaymentService:BaseUrl must be an absolute HTTP(S) URL.");
var paymentTimeout = builder.Configuration.GetValue<int>("PaymentService:TimeoutSeconds");
if (paymentTimeout is < 1 or > 30)
    throw new InvalidOperationException("PaymentService:TimeoutSeconds must be between 1 and 30.");
builder.Services.AddHttpClient<PaymentClient>(client =>
{
    client.BaseAddress = paymentUri;
    client.Timeout = TimeSpan.FromSeconds(paymentTimeout);
});

// Provide consistent problem-detail responses when requests fail.
builder.Services.AddProblemDetails();
// Record only request method/path, response status, and duration, not request or response bodies.
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath
        | HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration;
    options.CombineLogs = true;
});
// Allow browser requests from the configured frontend origin (or the local development default).
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration["Frontend:Origin"] ?? "http://localhost:5100")
        .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
// Apply cross-cutting request behavior before the service's routes handle each request.
app.UseHttpLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

// Create the order database schema if it does not exist yet. This is convenient for the
// single-instance demo; EnsureCreated does not migrate an existing database schema.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// Expose a small service-identification/health response and register the order API routes.
app.MapGet("/", () => Results.Ok(new { service = "OrderService", phase = 4 }));
app.MapOrderEndpoints();
app.Run();
