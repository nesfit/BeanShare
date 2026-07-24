using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BeanShare.Infrastructure.Persistence.Configurations;

public sealed class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> builder)
    {
        builder.ToTable("Purchases");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .ValueGeneratedNever();

        builder.Property("CoffeeStockId")
            .HasConversion<Guid>()
            .IsRequired();

        builder.OwnsOne(p => p.Product, pb =>
        {
            pb.Property(cp => cp.Name)
                .HasColumnName("ProductName")
                .HasMaxLength(100)
                .IsRequired();

            pb.Property(cp => cp.Brand)
                .HasColumnName("ProductBrand")
                .HasMaxLength(50)
                .IsRequired();

            pb.Property(cp => cp.Type)
                .HasColumnName("ProductType")
                .HasConversion<string>()
                .IsRequired();
        });

        builder.OwnsOne(p => p.Quantity, qb =>
        {
            qb.Property(w => w.Grams)
                .HasColumnName("QuantityGrams")
                .HasColumnType("decimal(10,1)")
                .IsRequired();
        });

        builder.OwnsOne(p => p.Cost, cb =>
        {
            cb.Property(m => m.Amount)
                .HasColumnName("CostAmount")
                .HasColumnType("decimal(10,2)")
                .IsRequired();

            cb.Property(m => m.Currency)
                .HasConversion(
                    currency => currency.Code,
                    code => Currency.Create(code))
                .HasColumnName("CostCurrency")
                .HasMaxLength(3)
                .IsRequired();
        });

        builder.Property(p => p.Vendor)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.PurchasedBy)
            .HasConversion(
                id => id.Value,
                value => (UserId)value)
            .IsRequired();

        builder.Property(p => p.PurchasedAt)
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        // --- Per-bag (lot) tracking ---
        builder.Property(p => p.Label)
            .HasMaxLength(20)
            .HasDefaultValue("")
            .IsRequired();

        builder.Property(p => p.ConsumedGrams)
            .HasColumnType("decimal(10,1)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(LotStatus.Sealed)
            .IsRequired();

        builder.Property(p => p.OpenedAt);
        builder.Property(p => p.DepletedAt);

        // Computed, non-persisted.
        builder.Ignore(p => p.RemainingGrams);
        builder.Ignore(p => p.UnitCostPerGram);
        builder.Ignore(p => p.CostPerGram);
        builder.Ignore(p => p.IsOpen);
        builder.Ignore(p => p.IsEmpty);

        builder.HasIndex(p => p.PurchasedAt);
        builder.HasIndex(p => p.CreatedAt);
        builder.HasIndex("CoffeeStockId");
        builder.HasIndex("CoffeeStockId", nameof(Purchase.PurchasedAt));
    }
}