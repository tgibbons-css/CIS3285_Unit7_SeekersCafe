using Microsoft.EntityFrameworkCore;

namespace OrderService;

/// <summary>
/// Provides Entity Framework access to OrderService's SQLite database and configures order relationships,
/// value conversions, and data constraints.
/// EF Core is the ORM (object-relational mapper): it maps these C# entities to relational tables,
/// translates LINQ queries to SQL, and tracks changes to be written by SaveChangesAsync.
/// </summary>
public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    // Querying Orders is the entry point for reading, adding, or changing orders.
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var order = modelBuilder.Entity<Order>();
        // Configure the order's key and column requirements, including string storage for its enum.
        order.HasKey(x => x.OrderId);
        order.Property(x => x.CustomerName).HasMaxLength(100).IsRequired();
        order.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        order.Property(x => x.TotalAmount).HasColumnName("TotalAmountCents")
            .HasConversion(value => checked((int)(value * 100m)), value => value / 100m);
        // SQLite stores timestamps without a DateTime Kind; restore UTC when reading.
        order.Property(x => x.OrderDate).HasConversion(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        // Each order owns its line items. Deleting an order also deletes its lines in the database.
        order.HasMany(x => x.OrderItems).WithOne().HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        order.ToTable(table => table.HasCheckConstraint("CK_Orders_Total", "TotalAmountCents >= 0"));

        var item = modelBuilder.Entity<OrderItem>();
        // Line items have their own key; MenuItemId refers to another service and is not a local FK.
        item.HasKey(x => x.OrderItemId);
        item.Property(x => x.ItemName).HasMaxLength(100).IsRequired();
        item.Property(x => x.UnitPrice).HasColumnName("UnitPriceCents")
            .HasConversion(value => checked((int)(value * 100m)), value => value / 100m);
        // Prevent duplicate lines for the same menu item within one order and enforce valid values.
        item.HasIndex(x => new { x.OrderId, x.MenuItemId }).IsUnique();
        item.ToTable(table =>
        {
            table.HasCheckConstraint("CK_OrderItems_Quantity", "Quantity >= 1 AND Quantity <= 20");
            table.HasCheckConstraint("CK_OrderItems_Price", "UnitPriceCents >= 0 AND UnitPriceCents <= 100000");
        });
    }
}
