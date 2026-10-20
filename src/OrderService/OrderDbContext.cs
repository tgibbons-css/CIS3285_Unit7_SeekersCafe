using Microsoft.EntityFrameworkCore;

namespace OrderService;

/// <summary>
/// Provides Entity Framework access to OrderService's SQLite database and configures order relationships,
/// value conversions, and data constraints.
/// </summary>
public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var order = modelBuilder.Entity<Order>();
        order.HasKey(x => x.OrderId);
        order.Property(x => x.CustomerName).HasMaxLength(100).IsRequired();
        order.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        order.Property(x => x.TotalAmount).HasColumnName("TotalAmountCents")
            .HasConversion(value => checked((int)(value * 100m)), value => value / 100m);
        // SQLite stores timestamps without a DateTime Kind; restore UTC when reading.
        order.Property(x => x.OrderDate).HasConversion(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        order.HasMany(x => x.OrderItems).WithOne().HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        order.ToTable(table => table.HasCheckConstraint("CK_Orders_Total", "TotalAmountCents >= 0"));

        var item = modelBuilder.Entity<OrderItem>();
        item.HasKey(x => x.OrderItemId);
        item.Property(x => x.ItemName).HasMaxLength(100).IsRequired();
        item.Property(x => x.UnitPrice).HasColumnName("UnitPriceCents")
            .HasConversion(value => checked((int)(value * 100m)), value => value / 100m);
        item.HasIndex(x => new { x.OrderId, x.MenuItemId }).IsUnique();
        item.ToTable(table =>
        {
            table.HasCheckConstraint("CK_OrderItems_Quantity", "Quantity >= 1 AND Quantity <= 20");
            table.HasCheckConstraint("CK_OrderItems_Price", "UnitPriceCents >= 0 AND UnitPriceCents <= 100000");
        });
    }
}
