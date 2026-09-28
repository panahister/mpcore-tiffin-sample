using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tiffin.Restaurants.Domain;

namespace Tiffin.Restaurants.Infrastructure.Persistence;

public sealed class RestaurantConfiguration : IEntityTypeConfiguration<Restaurant>
{
    public const string Schema = "restaurants";

    public void Configure(EntityTypeBuilder<Restaurant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("restaurants", Schema);
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.City).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Name).HasMaxLength(120).IsRequired();
        builder.Property(r => r.ManagerId).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();

        // Every read names the city first. The name is unique inside a city, not across the platform.
        builder.HasIndex(r => new { r.City, r.Name }).IsUnique().HasDatabaseName("ux_restaurants_city_name");
        builder.HasIndex(r => new { r.City, r.IsOpen }).HasDatabaseName("ix_restaurants_city_open");

        builder.OwnsMany(r => r.Menu, menu =>
        {
            menu.ToTable("menu_items", Schema);
            menu.WithOwner().HasForeignKey("RestaurantId");
            // The child entity's identity is its code.
            menu.Property(i => i.Id).HasColumnName("Code").HasMaxLength(32).IsRequired();
            menu.HasKey("RestaurantId", "Id");
            menu.Ignore(i => i.Code);
            menu.Property(i => i.Name).HasMaxLength(120).IsRequired();
            menu.Property(i => i.Price).HasPrecision(18, 2);
        });
        builder.Navigation(r => r.Menu).AutoInclude();

        // Two managers' sessions changing one menu: the second save fails on the row version.
        builder.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");
    }
}
