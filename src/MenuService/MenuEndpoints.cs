using Microsoft.EntityFrameworkCore;

namespace MenuService;

/// <summary>
/// Maps the HTTP endpoints for listing, retrieving, creating, updating, and deleting menu items,
/// including request validation and availability filtering.
/// EF Core is the ORM (object-relational mapper) used here: it translates C# queries and tracked
/// entity changes into database operations. The injected DbContext is the unit of work for each request.
/// </summary>
public static class MenuEndpoints
{
    public static void MapMenuEndpoints(this WebApplication app)
    {
        var menu = app.MapGroup("/api/menu");
        menu.MapGet("", async (bool? includeUnavailable, MenuDbContext db, CancellationToken ct) =>
        {
            // READ: build a database query, optionally filter unavailable items, and execute it
            // asynchronously. AsNoTracking is efficient here because this result is read-only.
            var query = db.MenuItems.AsNoTracking();
            if (includeUnavailable != true)
                query = query.Where(item => item.IsAvailable);
            return Results.Ok(await query.OrderBy(item => item.Category)
                .ThenBy(item => item.Name).ToListAsync(ct));
        });

        // OrderService can distinguish unavailable items from missing items.
        menu.MapGet("/{id:int}", async (int id, MenuDbContext db, CancellationToken ct) =>
        {
            // READ one row by its primary key; return 404 if the database has no matching item.
            var item = await db.MenuItems.AsNoTracking().SingleOrDefaultAsync(x => x.MenuItemId == id, ct);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        menu.MapPost("", async (MenuItemRequest request, MenuDbContext db, CancellationToken ct) =>
        {
            // CREATE: validate incoming data before constructing an entity or touching the database.
            var errors = request.Validate();
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var item = new MenuItem();
            request.ApplyTo(item);
            // Add marks the new entity for insertion; SaveChangesAsync sends the INSERT to SQLite.
            db.MenuItems.Add(item);
            await db.SaveChangesAsync(ct);
            app.Logger.LogInformation("Created menu item {MenuItemId}", item.MenuItemId);
            return Results.Created($"/api/menu/{item.MenuItemId}", item);
        });

        menu.MapPut("/{id:int}", async (int id, MenuItemRequest request, MenuDbContext db, CancellationToken ct) =>
        {
            // UPDATE: validate first, then load the existing row as a tracked entity.
            var errors = request.Validate();
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var item = await db.MenuItems.SingleOrDefaultAsync(x => x.MenuItemId == id, ct);
            if (item is null) return Results.NotFound();
            // Applying new values changes the tracked object; SaveChangesAsync persists the UPDATE.
            request.ApplyTo(item);
            await db.SaveChangesAsync(ct);
            app.Logger.LogInformation("Updated menu item {MenuItemId}", id);
            return Results.Ok(item);
        });

        menu.MapDelete("/{id:int}", async (int id, MenuDbContext db, CancellationToken ct) =>
        {
            // DELETE: load the row so a missing ID can return 404, then mark it for deletion.
            var item = await db.MenuItems.SingleOrDefaultAsync(x => x.MenuItemId == id, ct);
            if (item is null) return Results.NotFound();
            db.MenuItems.Remove(item);
            // The delete is issued when this request's unit of work is saved.
            await db.SaveChangesAsync(ct);
            app.Logger.LogInformation("Deleted menu item {MenuItemId}", id);
            return Results.NoContent();
        });
    }
}
