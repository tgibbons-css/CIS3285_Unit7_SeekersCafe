using Microsoft.EntityFrameworkCore;

namespace MenuService;

/// <summary>
/// Maps the HTTP endpoints for listing, retrieving, creating, updating, and deleting menu items,
/// including request validation and availability filtering.
/// </summary>
public static class MenuEndpoints
{
    public static void MapMenuEndpoints(this WebApplication app)
    {
        var menu = app.MapGroup("/api/menu");
        menu.MapGet("", async (bool? includeUnavailable, MenuDbContext db, CancellationToken ct) =>
        {
            var query = db.MenuItems.AsNoTracking();
            if (includeUnavailable != true)
                query = query.Where(item => item.IsAvailable);
            return Results.Ok(await query.OrderBy(item => item.Category)
                .ThenBy(item => item.Name).ToListAsync(ct));
        });

        // OrderService can distinguish unavailable items from missing items.
        menu.MapGet("/{id:int}", async (int id, MenuDbContext db, CancellationToken ct) =>
        {
            var item = await db.MenuItems.AsNoTracking().SingleOrDefaultAsync(x => x.MenuItemId == id, ct);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        menu.MapPost("", async (MenuItemRequest request, MenuDbContext db, CancellationToken ct) =>
        {
            var errors = request.Validate();
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var item = new MenuItem();
            request.ApplyTo(item);
            db.MenuItems.Add(item);
            await db.SaveChangesAsync(ct);
            app.Logger.LogInformation("Created menu item {MenuItemId}", item.MenuItemId);
            return Results.Created($"/api/menu/{item.MenuItemId}", item);
        });

        menu.MapPut("/{id:int}", async (int id, MenuItemRequest request, MenuDbContext db, CancellationToken ct) =>
        {
            var errors = request.Validate();
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var item = await db.MenuItems.SingleOrDefaultAsync(x => x.MenuItemId == id, ct);
            if (item is null) return Results.NotFound();
            request.ApplyTo(item);
            await db.SaveChangesAsync(ct);
            app.Logger.LogInformation("Updated menu item {MenuItemId}", id);
            return Results.Ok(item);
        });

        menu.MapDelete("/{id:int}", async (int id, MenuDbContext db, CancellationToken ct) =>
        {
            var item = await db.MenuItems.SingleOrDefaultAsync(x => x.MenuItemId == id, ct);
            if (item is null) return Results.NotFound();
            db.MenuItems.Remove(item);
            await db.SaveChangesAsync(ct);
            app.Logger.LogInformation("Deleted menu item {MenuItemId}", id);
            return Results.NoContent();
        });
    }
}
