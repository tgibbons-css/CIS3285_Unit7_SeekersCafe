using Microsoft.EntityFrameworkCore;

namespace PaymentService;

/// <summary>
/// Stores a simulated payment's order reference, request identifier, amount, method,
/// outcome, and transaction time for later retrieval and reconciliation.
/// </summary>
public sealed class Payment
{
    public int PaymentId { get; set; }
    public int OrderId { get; set; }
    public string RequestId { get; set; } = "";
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime TransactionDate { get; set; }
}

/// <summary>
/// Provides Entity Framework access to PaymentService's SQLite database, enforcing unique payments
/// per order and request identifier and converting decimal amounts to stored integer cents.
/// EF Core is the ORM (object-relational mapper): it maps Payment objects to database rows,
/// translates LINQ queries into SQL, and writes tracked changes when SaveChangesAsync is called.
/// This context is kept in Payment.cs alongside the payment entity rather than in a separate file.
/// </summary>
public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    // DbSet is the table-like entry point used by payment endpoint queries and inserts.
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var payment = modelBuilder.Entity<Payment>();
        payment.HasKey(x => x.PaymentId);
        // Unique indexes are enforced by SQLite, including when two requests arrive concurrently.
        // Constraints, not a check-then-insert alone, protect concurrent duplicate requests.
        payment.HasIndex(x => x.RequestId).IsUnique();
        payment.HasIndex(x => x.OrderId).IsUnique();
        // Specify required text columns and convert decimal dollars to integer cents for storage.
        payment.Property(x => x.RequestId).HasMaxLength(100).IsRequired();
        payment.Property(x => x.PaymentMethod).HasMaxLength(30).IsRequired();
        payment.Property(x => x.Status).HasMaxLength(20).IsRequired();
        payment.Property(x => x.Amount).HasColumnName("AmountCents")
            .HasConversion(value => checked((int)(value * 100m)), value => value / 100m);
        payment.Property(x => x.TransactionDate).HasConversion(value => value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        // Keep valid amount ranges in the database as well as in request validation.
        payment.ToTable(table => table.HasCheckConstraint("CK_Payments_Amount",
            "AmountCents >= 0 AND AmountCents <= 100000000"));
    }
}
