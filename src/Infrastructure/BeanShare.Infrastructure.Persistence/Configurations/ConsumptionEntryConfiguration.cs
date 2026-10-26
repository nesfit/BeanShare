using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BeanShare.Infrastructure.Persistence.Configurations;

internal sealed class ConsumptionEntryConfiguration : IEntityTypeConfiguration<ConsumptionEntry>
{
    public void Configure(EntityTypeBuilder<ConsumptionEntry> builder)
    {
        builder.ToTable("Consumptions");

        builder.Ignore(x => x.DomainEvents);

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                id => id.Value,
                value => new ConsumptionEntryId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.SpaceId)
            .HasConversion(
                id => id.Value,
                value => new SpaceId(value))
            .IsRequired();

        builder.Property(x => x.UserId)
            .HasConversion(
                id => id.Value,
                value => (UserId)value)
            .IsRequired();

        builder.OwnsOne(x => x.Product, productBuilder =>
        {
            productBuilder.WithOwner();

            productBuilder.Property(p => p.Name)
                .HasColumnName("ProductName")
                .HasMaxLength(200)
                .IsRequired();

            productBuilder.Property(p => p.Brand)
                .HasColumnName("ProductBrand")
                .HasMaxLength(200)
                .IsRequired();

            productBuilder.Property(p => p.Type)
                .HasColumnName("ProductType")
                .HasConversion<string>()
                .IsRequired();
        });

        builder.Property(x => x.Quantity)
            .HasConversion(
                quantity => quantity.Grams,
                grams => Weight.FromGrams(grams))
            .HasColumnType("numeric(10,3)")
            .HasColumnName("QuantityGrams")
            .IsRequired();

        builder.Property(x => x.ConsumedAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        // Configure nullable BillingPeriodId for billing period assignment
        builder.Property(x => x.BillingPeriodId)
            .HasConversion(
                new ValueConverter<BillingPeriodId?, Guid?>(
                    id => id.HasValue ? id.Value.Value : (Guid?)null,
                    value => value.HasValue ? new BillingPeriodId(value.Value) : (BillingPeriodId?)null))
            .IsRequired(false);

        builder.Property(x => x.PresetId)
            .HasConversion(
                id => id == null ? (Guid?)null : id.Value,
                value => value == null ? null : new PresetRecipeId(value.Value))
            .IsRequired(false);

        builder.Property(x => x.PresetName)
            .HasMaxLength(200)
            .IsRequired(false);

        // --- Per-bag cost lock-in (scalars; Money Cost is a computed projection of these) ---
        builder.Property(x => x.StockLotId)
            .IsRequired(false);

        builder.Property(x => x.CostAmount)
            .HasColumnType("numeric(18,4)")
            .IsRequired(false);

        builder.Property(x => x.CostCurrency)
            .HasMaxLength(3)
            .IsRequired(false);

        builder.Property(x => x.UnitCostPerGram)
            .HasColumnType("numeric(18,6)")
            .IsRequired(false);

        builder.Property(x => x.IsWriteOff)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.Version)
            .IsConcurrencyToken();

        builder.Ignore(x => x.Cost);

        builder.HasIndex(x => x.StockLotId)
            .HasDatabaseName("IX_Consumptions_StockLotId");

        builder.HasIndex(x => x.SpaceId)
            .HasDatabaseName("IX_Consumptions_SpaceId");

        builder.HasIndex(x => x.UserId)
            .HasDatabaseName("IX_Consumptions_UserId");

        builder.HasIndex(x => x.BillingPeriodId)
            .HasDatabaseName("IX_Consumptions_BillingPeriodId");

        builder.HasIndex(x => x.ConsumedAt)
            .HasDatabaseName("IX_Consumptions_ConsumedAt");

        builder.HasIndex(x => new { x.SpaceId, x.ConsumedAt })
            .HasDatabaseName("IX_Consumptions_SpaceId_ConsumedAt");
    }
}