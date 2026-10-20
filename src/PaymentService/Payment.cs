using Microsoft.EntityFrameworkCore;

namespace PaymentService;

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

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var payment = modelBuilder.Entity<Payment>();
        payment.HasKey(x => x.PaymentId);
        // Constraints, not a check-then-insert alone, protect concurrent duplicate requests.
        payment.HasIndex(x => x.RequestId).IsUnique();
        payment.HasIndex(x => x.OrderId).IsUnique();
        payment.Property(x => x.RequestId).HasMaxLength(100).IsRequired();
        payment.Property(x => x.PaymentMethod).HasMaxLength(30).IsRequired();
        payment.Property(x => x.Status).HasMaxLength(20).IsRequired();
        payment.Property(x => x.Amount).HasColumnName("AmountCents")
            .HasConversion(value => checked((int)(value * 100m)), value => value / 100m);
        payment.Property(x => x.TransactionDate).HasConversion(value => value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        payment.ToTable(table => table.HasCheckConstraint("CK_Payments_Amount",
            "AmountCents >= 0 AND AmountCents <= 100000000"));
    }
}
