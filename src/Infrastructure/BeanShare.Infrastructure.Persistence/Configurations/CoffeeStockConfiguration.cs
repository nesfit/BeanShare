using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BeanShare.Infrastructure.Persistence.Configurations;

public sealed class CoffeeStockConfiguration : IEntityTypeConfiguration<CoffeeStock>
{
    public void Configure(EntityTypeBuilder<CoffeeStock> builder)
    {
        builder.ToTable("CoffeeStocks");

        builder.HasKey(cs => cs.Id);

        builder.Property(cs => cs.Id)
            .HasConversion(
                id => id.Value,
                value => CoffeeStockId.From(value))
            .ValueGeneratedNever();

        builder.Property(cs => cs.SpaceId)
            .HasConversion(
                id => id.Value,
                value => (SpaceId)value)
            .IsRequired();

        builder.HasIndex(cs => cs.SpaceId)
            .IsUnique();

        builder.Property(cs => cs.CreatedAt)
            .IsRequired();

        builder.Property(cs => cs.UpdatedAt)
            .IsRequired();

        builder.HasMany(cs => cs.Purchases)
            .WithOne()
            .HasForeignKey("CoffeeStockId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(cs => cs.Purchases).UsePropertyAccessMode(PropertyAccessMode.Field);

        // StockLevels is now a derived read-only projection computed from Purchases — not persisted.
        builder.Ignore(cs => cs.StockLevels);
        builder.Ignore(cs => cs.DomainEvents);
    }
}