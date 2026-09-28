using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tiffin.Dispatch.Domain;

namespace Tiffin.Dispatch.Infrastructure.Persistence;

public sealed class CourierConfiguration : IEntityTypeConfiguration<Courier>
{
    public const string Schema = "dispatch";

    public void Configure(EntityTypeBuilder<Courier> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("couriers", Schema);
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("CourierId").HasMaxLength(64).ValueGeneratedNever();
        builder.Property(c => c.City).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(256).IsRequired();
        builder.Ignore(c => c.IsFree);

        // The roster: who is on duty and carries nothing, in a city, by how long they have waited.
        builder.HasIndex(c => new { c.City, c.IsOnDuty, c.FreeSinceUtc }).HasDatabaseName("ix_couriers_roster");

        // Two orders reaching for one courier: the second save fails on the row version.
        builder.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");
    }
}

public sealed class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("deliveries", CourierConfiguration.Schema);
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("OrderId").ValueGeneratedNever();
        builder.Ignore(d => d.OrderId);
        builder.Property(d => d.City).HasMaxLength(64).IsRequired();
        builder.Property(d => d.OrderNumber).HasMaxLength(24).IsRequired();
        builder.Property(d => d.CustomerId).HasMaxLength(64).IsRequired();
        builder.Property(d => d.CourierId).HasMaxLength(64).IsRequired();
        builder.Property(d => d.CourierName).HasMaxLength(256).IsRequired();
        builder.Property(d => d.RestaurantName).HasMaxLength(120).IsRequired();
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(d => new { d.City, d.CourierId, d.Status }).HasDatabaseName("ix_deliveries_city_courier_status");

        // A value object stored in the delivery's own row.
        builder.OwnsOne(d => d.Destination, to =>
        {
            to.Property(a => a.Recipient).HasColumnName("To_Recipient").HasMaxLength(100);
            to.Property(a => a.Phone).HasColumnName("To_Phone").HasMaxLength(20);
            to.Property(a => a.District).HasColumnName("To_District").HasMaxLength(60);
            to.Property(a => a.Line).HasColumnName("To_Line").HasMaxLength(300);
        });
        builder.Navigation(d => d.Destination).IsRequired();

        builder.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");
    }
}
