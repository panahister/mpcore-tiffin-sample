using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tiffin.Ordering.Domain;

namespace Tiffin.Ordering.Infrastructure.Persistence;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public const string Schema = "ordering";

    public void Configure(EntityTypeBuilder<Order> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("orders", Schema);
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.OrderNumber).HasMaxLength(24).IsRequired();
        builder.HasIndex(o => o.OrderNumber).IsUnique().HasDatabaseName("ux_orders_order_number");
        builder.Property(o => o.City).HasMaxLength(64).IsRequired();
        builder.Property(o => o.CustomerId).HasMaxLength(64).IsRequired();
        builder.Property(o => o.CustomerName).HasMaxLength(256).IsRequired();
        builder.Property(o => o.RestaurantName).HasMaxLength(120).IsRequired();
        builder.Property(o => o.Currency).HasMaxLength(3).IsRequired();
        builder.Property(o => o.Total).HasPrecision(18, 2);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(o => o.CancellationReason).HasMaxLength(40);
        builder.Property(o => o.PaymentReference).HasMaxLength(64);
        builder.Property(o => o.RefundReference).HasMaxLength(64);
        builder.Property(o => o.CourierId).HasMaxLength(64);
        builder.Property(o => o.CourierName).HasMaxLength(256);
        builder.Ignore(o => o.IsCancelled);
        builder.Ignore(o => o.CanMarkPaid);
        builder.Ignore(o => o.CanAccept);
        builder.Ignore(o => o.CanSendOut);
        builder.Ignore(o => o.CanDeliver);
        builder.Ignore(o => o.CanCancel);
        builder.Ignore(o => o.CanBeCancelledByCustomer);
        builder.Ignore(o => o.OwesARefund);

        // Every read names the city first: a customer's orders, newest first.
        builder.HasIndex(o => new { o.City, o.CustomerId, o.PlacedOnUtc }).HasDatabaseName("ix_orders_city_customer_placed");

        // A value object stored in the order's own row; Entity Framework rebuilds it through its constructor.
        builder.OwnsOne(o => o.DeliverTo, address =>
        {
            address.Property(a => a.Recipient).HasColumnName("DeliverTo_Recipient").HasMaxLength(100);
            address.Property(a => a.Phone).HasColumnName("DeliverTo_Phone").HasMaxLength(20);
            address.Property(a => a.District).HasColumnName("DeliverTo_District").HasMaxLength(60);
            address.Property(a => a.Line).HasColumnName("DeliverTo_Line").HasMaxLength(300);
        });
        builder.Navigation(o => o.DeliverTo).IsRequired();

        builder.OwnsMany(o => o.Lines, lines =>
        {
            lines.ToTable("order_lines", Schema);
            lines.WithOwner().HasForeignKey("OrderId");
            lines.HasKey("OrderId", "Code");
            lines.Property(l => l.Code).HasMaxLength(32).IsRequired();
            lines.Property(l => l.Name).HasMaxLength(120).IsRequired();
            lines.Property(l => l.UnitPrice).HasPrecision(18, 2);
            lines.Ignore(l => l.LineTotal);
        });
        builder.Navigation(o => o.Lines).AutoInclude();

        builder.OwnsMany(o => o.History, history =>
        {
            history.ToTable("order_history", Schema);
            history.WithOwner().HasForeignKey("OrderId");
            history.Property<int>("Sequence").ValueGeneratedOnAdd();
            history.HasKey("OrderId", "Sequence");
            history.Property(h => h.Status).HasConversion<string>().HasMaxLength(16);
            history.Property(h => h.Note).HasMaxLength(300);
        });
        builder.Navigation(o => o.History).AutoInclude();

        // An answer from the Kitchen and the customer's cancellation at the same moment: the second save
        // fails on the row version, is retried, and meets the order as the first one left it.
        builder.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");
    }
}
