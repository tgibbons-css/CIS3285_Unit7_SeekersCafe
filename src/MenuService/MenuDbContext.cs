using Microsoft.EntityFrameworkCore;

namespace MenuService;

/// <summary>
/// Provides Entity Framework access to MenuService's SQLite database and configures menu item
/// constraints and the conversion between decimal prices and stored integer cents.
/// </summary>
public sealed class MenuDbContext(DbContextOptions<MenuDbContext> options) : DbContext(options)
{
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var item = modelBuilder.Entity<MenuItem>();
        item.HasKey(x => x.MenuItemId);
        item.Property(x => x.Name).HasMaxLength(100).IsRequired();
        item.Property(x => x.Description).HasMaxLength(500).IsRequired();
        item.Property(x => x.Category).HasMaxLength(50).IsRequired();
        // Decimal dollars in C#, exact integer cents in SQLite.
        item.Property(x => x.Price).HasColumnName("PriceCents")
            .HasConversion(value => checked((int)(value * 100m)), value => value / 100m);
        item.ToTable(table => table.HasCheckConstraint("CK_MenuItems_PriceCents",
            "PriceCents >= 0 AND PriceCents <= 100000"));
    }
}
