using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MPCore.Application.Querying;
using Tiffin.Notifications.Application.Ports;
using Tiffin.Notifications.Application.Views;
using Tiffin.Notifications.Domain;

namespace Tiffin.Notifications.Infrastructure.Persistence;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public const string Schema = "notifications";

    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("notifications", Schema);
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).HasColumnName("EventId").ValueGeneratedNever();
        builder.Property(n => n.City).HasMaxLength(64).IsRequired();
        builder.Property(n => n.RecipientId).HasMaxLength(64).IsRequired();
        builder.Property(n => n.MessageKey).HasMaxLength(100).IsRequired();

        // The values of the message, as a JSON document in one column: they are read together and never
        // searched. The field behind the property is what Entity Framework fills.
        builder.Ignore(n => n.Arguments);
        builder.Property<Dictionary<string, string>>("arguments")
            .HasColumnName("Arguments").HasColumnType("jsonb")
            .HasConversion(
                static values => JsonSerializer.Serialize(values, JsonSerializerOptions.Default),
                static text => JsonSerializer.Deserialize<Dictionary<string, string>>(text, JsonSerializerOptions.Default) ?? new Dictionary<string, string>(),
                new ValueComparer<Dictionary<string, string>>(
                    static (a, b) => a!.Count == b!.Count && !a.Except(b).Any(),
                    static values => values.Aggregate(0, static (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value)),
                    static values => new Dictionary<string, string>(values)));

        // What somebody is shown: their own, in their city, newest first, and the unread ones first of all.
        builder.HasIndex(n => new { n.City, n.RecipientId, n.OccurredOnUtc }).HasDatabaseName("ix_notifications_city_recipient_time");
    }
}

public sealed class NotificationRepository(AppDbContext database) : INotificationRepository
{
    public async Task<Notification?> GetAsync(Guid id, string city, CancellationToken cancellationToken) =>
        await database.Set<Notification>().FirstOrDefaultAsync(n => n.Id == id && n.City == city, cancellationToken).ConfigureAwait(false);

    public void Add(Notification notification) => database.Set<Notification>().Add(notification);
}

/// <summary>The read side: read without tracking, mapped to a view before it leaves.</summary>
public sealed class NotificationReadModel(AppDbContext database) : INotificationReadModel
{
    public async Task<Page<NotificationView>> ListForAsync(
        string recipientId, string city, bool unreadOnly, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        var query = database.Set<Notification>().AsNoTracking().Where(n => n.City == city && n.RecipientId == recipientId);
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadOnUtc == null);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var found = await query.OrderByDescending(n => n.OccurredOnUtc).Skip(page.Skip).Take(page.Size).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Page<NotificationView>([.. found.Select(NotificationViews.Of)], page.Number, page.Size, total);
    }
}
