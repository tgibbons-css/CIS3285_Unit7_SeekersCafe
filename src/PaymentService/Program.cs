using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PaymentService;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
var connection = new SqliteConnectionStringBuilder(
    builder.Configuration.GetConnectionString("PaymentDatabase")
    ?? throw new InvalidOperationException("PaymentDatabase connection string is required."));
connection.DataSource = Path.GetFullPath(connection.DataSource, builder.Environment.ContentRootPath);
builder.Services.AddDbContext<PaymentDbContext>(options => options.UseSqlite(connection.ToString()));
var delay = builder.Configuration.GetValue<int>("Simulation:ResponseDelayMilliseconds");
if (delay is < 0 or > 10000)
    throw new InvalidOperationException("Simulation:ResponseDelayMilliseconds must be from 0 to 10000.");
builder.Services.AddProblemDetails();
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath
        | HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration;
    options.CombineLogs = true;
});

var app = builder.Build();
app.UseHttpLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    await db.Database.EnsureCreatedAsync();
}
app.MapGet("/", () => Results.Ok(new { service = "PaymentService", phase = 4 }));
app.MapPaymentEndpoints(delay, builder.Configuration.GetValue<bool>("Simulation:DeclinePayments"));
app.Run();
