using MenuService;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Console JSON works in VS Code and containers without Windows Event Log permissions.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
// Return 400 for JSON binding failures in both Development and Production.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

// No other service receives this connection string or accesses this database.
var connection = new SqliteConnectionStringBuilder(
    builder.Configuration.GetConnectionString("MenuDatabase")
    ?? throw new InvalidOperationException("MenuDatabase connection string is required."));
// Resolve relative files against this service rather than the terminal's directory.
connection.DataSource = Path.GetFullPath(connection.DataSource, builder.Environment.ContentRootPath);
builder.Services.AddDbContext<MenuDbContext>(options => options.UseSqlite(connection.ToString()));
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

// One demo instance owns its schema. EnsureCreated cannot upgrade an existing schema.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MenuDbContext>();
    if (await db.Database.EnsureCreatedAsync())
    {
        var items = MenuSeed.CreateItems();
        db.MenuItems.AddRange(items);
        await db.SaveChangesAsync();
        app.Logger.LogInformation("Created MenuService database and seeded {Count} sample items", items.Length);
    }
}

app.MapGet("/", () => Results.Ok(new { service = "MenuService", phase = 2 }));
app.MapMenuEndpoints();
app.Run();
