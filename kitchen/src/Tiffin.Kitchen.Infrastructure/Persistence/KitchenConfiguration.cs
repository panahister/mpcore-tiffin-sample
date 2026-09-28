using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tiffin.Kitchen.Domain;

namespace Tiffin.Kitchen.Infrastructure.Persistence;

public sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public const string Schema = "kitchen";

    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tickets", Schema);
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("OrderId").ValueGeneratedNever();
        builder.Ignore(t => t.OrderId);
        builder.Property(t => t.City).HasMaxLength(64).IsRequired();
        builder.Property(t => t.OrderNumber).HasMaxLength(24).IsRequired();
        builder.Property(t => t.RestaurantName).HasMaxLength(120).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.Reason).HasMaxLength(200);

        // A manager's work list: the tickets of a restaurant, by status, oldest first.
        builder.HasIndex(t => new { t.City, t.RestaurantId, t.Status, t.ReceivedOnUtc }).HasDatabaseName("ix_tickets_city_restaurant_status");

        builder.OwnsMany(t => t.Lines, lines =>
        {
            lines.ToTable("ticket_lines", Schema);
            lines.WithOwner().HasForeignKey("OrderId");
            lines.HasKey("OrderId", "Code");
            lines.Property(l => l.Code).HasMaxLength(32).IsRequired();
            lines.Property(l => l.Name).HasMaxLength(120).IsRequired();
        });
        builder.Navigation(t => t.Lines).AutoInclude();

        // The manager accepts while the order's cancellation arrives: the second save fails on the row version.
        builder.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");
    }
}

public sealed class KnownRestaurantConfiguration : IEntityTypeConfiguration<KnownRestaurant>
{
    public void Configure(EntityTypeBuilder<KnownRestaurant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("restaurants", TicketConfiguration.Schema);
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("RestaurantId").ValueGeneratedNever();
        builder.Property(r => r.City).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Name).HasMaxLength(120).IsRequired();
        builder.Property(r => r.ManagerId).HasMaxLength(64).IsRequired();
        builder.HasIndex(r => new { r.City, r.ManagerId }).HasDatabaseName("ix_restaurants_city_manager");
    }
}
