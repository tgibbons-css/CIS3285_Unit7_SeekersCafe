var builder = WebApplication.CreateBuilder(args);
var services = new Dictionary<string, string>();
foreach (var key in new[] { "MenuBaseUrl", "OrderBaseUrl" })
{
    var value = builder.Configuration[$"Services:{key}"];
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        throw new InvalidOperationException($"Services:{key} must be an absolute HTTP(S) URL.");
    services[key] = uri.GetLeftPart(UriPartial.Authority);
}
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
// Browser-reachable URLs only; this host neither proxies requests nor owns business data.
app.MapGet("/api/config", () => Results.Ok(new
{
    menuBaseUrl = services["MenuBaseUrl"], orderBaseUrl = services["OrderBaseUrl"]
}));
app.MapGet("/health", () => Results.Ok(new { service = "WebApp", phase = 5 }));
app.Run();
