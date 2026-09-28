using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tiffin.Media.Application.Ports;
using Tiffin.Media.Domain;

namespace Tiffin.Media.Infrastructure.Persistence;

public sealed class MediaFileConfiguration : IEntityTypeConfiguration<MediaFile>
{
    public const string Schema = "media";

    public void Configure(EntityTypeBuilder<MediaFile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("files", Schema);
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.City).HasMaxLength(64).IsRequired();
        builder.Property(f => f.OwnerId).HasMaxLength(64).IsRequired();
        builder.Property(f => f.Purpose).HasMaxLength(40).IsRequired();
        builder.Property(f => f.FileName).HasMaxLength(200).IsRequired();
        builder.Property(f => f.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(f => f.StorageKey).HasMaxLength(200).IsRequired();
        builder.Property(f => f.State).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(f => f.StorageKey).IsUnique().HasDatabaseName("ux_files_storage_key");
        builder.HasIndex(f => new { f.City, f.OwnerId }).HasDatabaseName("ix_files_city_owner");
        // What the sweeper looks for: places that were reserved and never filled.
        builder.HasIndex(f => f.ExpiresOnUtc).HasFilter("\"State\" = 'Pending'").HasDatabaseName("ix_files_places_to_sweep");

        builder.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");
    }
}

public sealed class MediaRepository(AppDbContext database) : IMediaRepository
{
    public async Task<MediaFile?> GetAsync(Guid id, string city, CancellationToken cancellationToken) =>
        await database.Set<MediaFile>().FirstOrDefaultAsync(f => f.Id == id && f.City == city, cancellationToken).ConfigureAwait(false);

    public void Add(MediaFile file) => database.Set<MediaFile>().Add(file);
}

/// <summary>The read side: read without tracking.</summary>
public sealed class MediaReadModel(AppDbContext database) : IMediaReadModel
{
    public async Task<MediaFile?> FindAsync(Guid id, string city, CancellationToken cancellationToken) =>
        await database.Set<MediaFile>().AsNoTracking().FirstOrDefaultAsync(f => f.Id == id && f.City == city, cancellationToken).ConfigureAwait(false);
}
