using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tiffin.Payments.Domain;

namespace Tiffin.Payments.Infrastructure.Persistence;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public const string Schema = "payments";

    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("payments", Schema);
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.City).HasMaxLength(64).IsRequired();
        builder.Property(p => p.CustomerId).HasMaxLength(64).IsRequired();
        builder.Property(p => p.PaymentToken).HasMaxLength(200);
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.ProviderReference).HasMaxLength(64);
        builder.Property(p => p.RefundReference).HasMaxLength(64);
        builder.Property(p => p.DeclineCode).HasMaxLength(64);

        // One payment per order: the second attempt to open one fails here, and finds the first.
        builder.HasIndex(p => p.OrderId).IsUnique().HasDatabaseName("ux_payments_order");
        // What the eraser looks for: tokens that are still there after their time.
        builder.HasIndex(p => p.ExpiresOnUtc).HasFilter("\"PaymentToken\" IS NOT NULL").HasDatabaseName("ix_payments_tokens_to_erase");

        // A request to charge and a request to refund at the same moment: the second save fails on the
        // row version, is retried, and meets the payment as the first one left it.
        builder.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");
    }
}
