using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OrderService;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<OrderStatus>(allowIntegerValues: false)));

// OrderService owns this file. Menu data is obtained through HTTP, never its database.
var connection = new SqliteConnectionStringBuilder(
    builder.Configuration.GetConnectionString("OrderDatabase")
    ?? throw new InvalidOperationException("OrderDatabase connection string is required."));
connection.DataSource = Path.GetFullPath(connection.DataSource, builder.Environment.ContentRootPath);
builder.Services.AddDbContext<OrderDbContext>(options => options.UseSqlite(connection.ToString()));

var menuUrl = builder.Configuration["MenuService:BaseUrl"];
if (!Uri.TryCreate(menuUrl, UriKind.Absolute, out var menuUri)
    || (menuUri.Scheme != "http" && menuUri.Scheme != "https"))
    throw new InvalidOperationException("MenuService:BaseUrl must be an absolute HTTP(S) URL.");
var timeout = builder.Configuration.GetValue<int>("MenuService:TimeoutSeconds");
if (timeout is < 1 or > 30)
    throw new InvalidOperationException("MenuService:TimeoutSeconds must be between 1 and 30.");
builder.Services.AddHttpClient<MenuClient>(client =>
{
    client.BaseAddress = menuUri;
    client.Timeout = TimeSpan.FromSeconds(timeout);
});

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

builder.Services.AddProblemDetails();
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath
        | HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration;
    options.CombineLogs = true;
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration["Frontend:Origin"] ?? "http://localhost:5100")
        .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseHttpLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.MapGet("/", () => Results.Ok(new { service = "OrderService", phase = 4 }));
app.MapOrderEndpoints();
app.Run();
