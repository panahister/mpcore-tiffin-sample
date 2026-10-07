using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MPCore.Application.Querying;
using Tiffin.Access.Application.Ports;

namespace Tiffin.Access.Infrastructure.Persistence;

public sealed class IdentityPerson
{
    private IdentityPerson()
    {
    }

    public string PersonId { get; private set; } = string.Empty;
    public string UserName { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? City { get; private set; }
    public string[] Roles { get; private set; } = [];
    public bool Enabled { get; private set; }
    public string LastEventId { get; private set; } = string.Empty;
    public DateTimeOffset LastEventOccurredOnUtc { get; private set; }

    public static IdentityPerson From(Person person, string eventId, DateTimeOffset occurredAt) => new()
    {
        PersonId = person.PersonId,
        UserName = person.UserName,
        Name = person.Name,
        City = person.City,
        Roles = [.. person.Roles.Order(StringComparer.Ordinal)],
        Enabled = person.Enabled,
        LastEventId = eventId,
        LastEventOccurredOnUtc = occurredAt
    };

    public bool IsNewer(string eventId, DateTimeOffset occurredAt) =>
        occurredAt > LastEventOccurredOnUtc
        || occurredAt == LastEventOccurredOnUtc && string.CompareOrdinal(eventId, LastEventId) > 0;

    public void Refresh(Person person, string eventId, DateTimeOffset occurredAt)
    {
        if (!IsNewer(eventId, occurredAt))
        {
            return;
        }

        UserName = person.UserName;
        Name = person.Name;
        City = person.City;
        Roles = [.. person.Roles.Order(StringComparer.Ordinal)];
        Enabled = person.Enabled;
        LastEventId = eventId;
        LastEventOccurredOnUtc = occurredAt;
    }

    public void Disable(string eventId, DateTimeOffset occurredAt)
    {
        if (!IsNewer(eventId, occurredAt))
        {
            return;
        }

        Enabled = false;
        LastEventId = eventId;
        LastEventOccurredOnUtc = occurredAt;
    }

    public Person ToPerson() => new(PersonId, UserName, Name, City, Roles, Enabled);
}

public sealed class IdentityPersonConfiguration : IEntityTypeConfiguration<IdentityPerson>
{
    public void Configure(EntityTypeBuilder<IdentityPerson> builder)
    {
        builder.ToTable("identity_people", GrantConfiguration.Schema);
        builder.HasKey(person => person.PersonId);
        builder.Property(person => person.PersonId).HasMaxLength(128).ValueGeneratedNever();
        builder.Property(person => person.UserName).HasMaxLength(256).IsRequired();
        builder.Property(person => person.Name).HasMaxLength(256).IsRequired();
        builder.Property(person => person.City).HasMaxLength(64);
        builder.Property(person => person.Roles).HasColumnType("text[]").IsRequired();
        builder.Property(person => person.LastEventId).HasMaxLength(160).IsRequired();
        builder.Property(person => person.LastEventOccurredOnUtc).IsRequired();
        builder.HasIndex(person => new { person.City, person.UserName })
            .HasDatabaseName("ix_identity_people_city_username");
    }
}

public sealed class IdentityProjection(AppDbContext database) : IIdentityProjection
{
    public async Task UpsertAsync(Person person, string eventId, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        var current = await database.Set<IdentityPerson>().FindAsync([person.PersonId], cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            database.Add(IdentityPerson.From(person, eventId, occurredAt));
        }
        else
        {
            current.Refresh(person, eventId, occurredAt);
        }
    }

    public async Task DisableAsync(string personId, string eventId, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        var current = await database.Set<IdentityPerson>().FindAsync([personId], cancellationToken).ConfigureAwait(false);
        current?.Disable(eventId, occurredAt);
    }

    public async Task<Person?> FindAsync(string personId, CancellationToken cancellationToken)
    {
        var current = await database.Set<IdentityPerson>().AsNoTracking()
            .FirstOrDefaultAsync(person => person.PersonId == personId && person.Enabled, cancellationToken)
            .ConfigureAwait(false);
        return current?.ToPerson();
    }

    public async Task<Page<Person>> PeopleOfAsync(string city, PageRequest page, CancellationToken cancellationToken)
    {
        var query = database.Set<IdentityPerson>().AsNoTracking()
            .Where(person => person.Enabled && person.City == city);
        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var people = await query.OrderBy(person => person.UserName)
            .Skip(page.Skip).Take(page.Size).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Page<Person>([.. people.Select(person => person.ToPerson())], page.Number, page.Size, total);
    }
}
