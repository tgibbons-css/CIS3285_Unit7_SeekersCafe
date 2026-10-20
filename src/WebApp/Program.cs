// Top-level statements are this application's entry point; C# generates the Program class.
// The builder loads configuration (including appsettings files, environment variables,
// and command-line arguments) and prepares the services used by the web host.
var builder = WebApplication.CreateBuilder(args);

// Store the validated service addresses that will be shared with the browser.
var services = new Dictionary<string, string>();
// WebApp needs MenuService for browsing and OrderService for checkout and order tracking.
foreach (var key in new[] { "MenuBaseUrl", "OrderBaseUrl" })
{
    // A colon selects a nested configuration key, such as Services:MenuBaseUrl.
    var value = builder.Configuration[$"Services:{key}"];
    // Require a complete HTTP(S) address; stop startup early if configuration is invalid.
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        throw new InvalidOperationException($"Services:{key} must be an absolute HTTP(S) URL.");
    // Keep the scheme, hostname, and optional port; discard any path, query, or fragment.
    // The JavaScript code adds the appropriate API path when it makes a request.
    services[key] = uri.GetLeftPart(UriPartial.Authority);
}

// Build the application so we can configure how it handles incoming HTTP requests.
var app = builder.Build();

// Middleware runs in registration order. For a directory request such as "/",
// default-file middleware rewrites the path to an existing default page such as index.html.
app.UseDefaultFiles();
// Serve files from wwwroot, including index.html, styles.css, and the JavaScript modules.
// This follows UseDefaultFiles so the rewritten default-page path can be served.
app.UseStaticFiles();

// GET /api/config returns JSON containing browser-reachable service addresses.
// The browser calls those services directly; WebApp hosts the UI and owns no business database.
// Cross-origin access is allowed by the API services' CORS configuration.
app.MapGet("/api/config", () => Results.Ok(new
{
    // These property names match what app.mjs expects when loading configuration.
    menuBaseUrl = services["MenuBaseUrl"], orderBaseUrl = services["OrderBaseUrl"]
}));

// GET /health identifies this running host. It does not check the other services or databases.
app.MapGet("/health", () => Results.Ok(new { service = "WebApp", phase = 5 }));

// Start listening for requests and keep the application running until it is shut down.
app.Run();
