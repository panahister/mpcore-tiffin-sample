using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tiffin.Tracking.Application.Ports;
using Tiffin.Tracking.Application.Views;
using Tiffin.Tracking.Domain;

namespace Tiffin.Tracking.Infrastructure.Persistence;

public sealed class TrackedDeliveryConfiguration : IEntityTypeConfiguration<TrackedDelivery>
{
    public const string Schema = "tracking";

    public void Configure(EntityTypeBuilder<TrackedDelivery> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("deliveries", Schema);
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("order_id").ValueGeneratedNever();
        builder.Ignore(d => d.OrderId);
        builder.Property(d => d.City).HasColumnName("city").HasMaxLength(64).IsRequired();
        builder.Property(d => d.OrderNumber).HasColumnName("order_number").HasMaxLength(24).IsRequired();
        builder.Property(d => d.CustomerId).HasColumnName("customer_id").HasMaxLength(64).IsRequired();
        builder.Property(d => d.CourierId).HasColumnName("courier_id").HasMaxLength(64).IsRequired();
        builder.Property(d => d.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
        builder.Property(d => d.LastLatitude).HasColumnName("last_latitude");
        builder.Property(d => d.LastLongitude).HasColumnName("last_longitude");
        builder.Property(d => d.LastSeenOnUtc).HasColumnName("last_seen_on_utc");
        builder.Property(d => d.PositionCount).HasColumnName("position_count");
        builder.Property(d => d.BeganOnUtc).HasColumnName("began_on_utc");
        builder.Property(d => d.ArrivedOnUtc).HasColumnName("arrived_on_utc");
        builder.Property(d => d.CreatedOnUtc).HasColumnName("recorded_on_utc");
        builder.Property(d => d.ModifiedOnUtc).HasColumnName("modified_on_utc");

        // Two positions of one delivery at the same moment: the second save fails on the row version.
        builder.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");
    }
}

public sealed class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public const string Table = "positions";

    /// <summary>The column the hypertable is partitioned on.</summary>
    public const string TimeColumn = "recorded_on_utc";

    public void Configure(EntityTypeBuilder<Position> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(Table, TrackedDeliveryConfiguration.Schema);

        // TimescaleDB partitions a hypertable on its time column, and every unique index has to contain
        // that column. A delivery is seen at one place at one moment: the two together are the key.
        builder.HasKey(p => new { p.OrderId, p.RecordedOnUtc });
        builder.Property(p => p.OrderId).HasColumnName("order_id");
        builder.Property(p => p.RecordedOnUtc).HasColumnName(TimeColumn);
        builder.Property(p => p.City).HasColumnName("city").HasMaxLength(64).IsRequired();
        builder.Property(p => p.CourierId).HasColumnName("courier_id").HasMaxLength(64).IsRequired();
        builder.Property(p => p.Latitude).HasColumnName("latitude");
        builder.Property(p => p.Longitude).HasColumnName("longitude");
    }
}

public sealed class TrackingRepository(AppDbContext database) : ITrackingRepository
{
    public async Task<TrackedDelivery?> GetAsync(Guid orderId, string city, CancellationToken cancellationToken) =>
        await database.Set<TrackedDelivery>().FirstOrDefaultAsync(d => d.Id == orderId && d.City == city, cancellationToken).ConfigureAwait(false);

    public void Add(TrackedDelivery delivery) => database.Set<TrackedDelivery>().Add(delivery);

    public void Log(Position position) => database.Set<Position>().Add(position);
}

/// <summary>The read side: read without tracking, mapped to a view before it leaves.</summary>
public sealed class TrackingReadModel(AppDbContext database) : ITrackingReadModel
{
    public async Task<TrackedDelivery?> FindAsync(Guid orderId, string city, CancellationToken cancellationToken) =>
        await database.Set<TrackedDelivery>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == orderId && d.City == city, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<PositionView>> TrailAsync(Guid orderId, string city, int points, CancellationToken cancellationToken) =>
        await database.Set<Position>().AsNoTracking()
            .Where(p => p.OrderId == orderId && p.City == city)
            .OrderByDescending(p => p.RecordedOnUtc)
            .Take(points)
            .Select(p => new PositionView(p.Latitude, p.Longitude, p.RecordedOnUtc))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}
