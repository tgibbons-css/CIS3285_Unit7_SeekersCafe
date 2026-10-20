var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Phase 1: identity only. Business endpoints are added in later phases.
app.MapGet("/", () => Results.Ok(new { service = "WebApp", phase = 1 }));

app.Run();
