using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MPCore.Application.Querying;
using Tiffin.Access.Application.Ports;
using Tiffin.Access.Application.Views;
using Tiffin.Access.Domain;

namespace Tiffin.Access.Infrastructure.Persistence;

public sealed class GrantConfiguration : IEntityTypeConfiguration<Grant>
{
    public const string Schema = "access";

    public void Configure(EntityTypeBuilder<Grant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("grants", Schema);
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.City).HasMaxLength(64).IsRequired();
        builder.Property(g => g.PersonId).HasMaxLength(64).IsRequired();
        builder.Property(g => g.PersonName).HasMaxLength(256).IsRequired();
        builder.Property(g => g.Role).HasMaxLength(64).IsRequired();
        builder.Property(g => g.Kind).HasConversion<string>().HasMaxLength(8);
        builder.Property(g => g.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(g => g.DecidedBy).HasMaxLength(64).IsRequired();
        builder.Property(g => g.Failure).HasMaxLength(300);
        builder.HasIndex(g => new { g.City, g.DecidedOnUtc }).HasDatabaseName("ix_grants_city_decided");
        builder.HasIndex(g => new { g.City, g.PersonId }).HasDatabaseName("ix_grants_city_person");
    }
}

public sealed class GrantRepository(AppDbContext database) : IGrantRepository
{
    public async Task<Grant?> GetAsync(Guid id, string city, CancellationToken cancellationToken) =>
        await database.Set<Grant>().FirstOrDefaultAsync(g => g.Id == id && g.City == city, cancellationToken).ConfigureAwait(false);

    public void Add(Grant grant) => database.Set<Grant>().Add(grant);
}

/// <summary>The read side: read without tracking, mapped to a view before it leaves.</summary>
public sealed class GrantReadModel(AppDbContext database) : IGrantReadModel
{
    public async Task<GrantView?> FindAsync(Guid id, string city, CancellationToken cancellationToken)
    {
        var grant = await database.Set<Grant>().AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id && g.City == city, cancellationToken).ConfigureAwait(false);
        return grant is null ? null : AccessViews.Of(grant);
    }

    public async Task<Page<GrantView>> ListAsync(string city, string? personId, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        var query = database.Set<Grant>().AsNoTracking().Where(g => g.City == city);
        if (!string.IsNullOrWhiteSpace(personId))
        {
            query = query.Where(g => g.PersonId == personId);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var grants = await query.OrderByDescending(g => g.DecidedOnUtc).Skip(page.Skip).Take(page.Size).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Page<GrantView>([.. grants.Select(AccessViews.Of)], page.Number, page.Size, total);
    }
}
