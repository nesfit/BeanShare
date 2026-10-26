using BeanShare.Domain.Aggregates.History;
using BeanShare.Domain.Common;
using BeanShare.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BeanShare.Infrastructure.Persistence.Configurations;

public sealed class HistoryImportConfiguration : IEntityTypeConfiguration<HistoryImport>
{
    public void Configure(EntityTypeBuilder<HistoryImport> builder)
    {
        builder.ToTable("HistoryImports");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Id)
            .HasConversion(id => id.Value, value => new HistoryImportId(value))
            .ValueGeneratedNever();

        builder.Property(h => h.SpaceId)
            .HasConversion(id => id.Value, value => new SpaceId(value))
            .IsRequired();

        builder.Property(h => h.ImportedBy)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(h => h.ImportedAt)
            .IsRequired();

        builder.Property(h => h.Note)
            .HasMaxLength(HistoryImport.MaxNoteLength)
            .IsRequired();

        builder.Ignore(h => h.DomainEvents);

        builder.HasIndex(h => h.SpaceId);

        builder.OwnsMany(h => h.CupCounts, counts =>
        {
            counts.ToTable("HistoricalCupCounts");
            counts.WithOwner().HasForeignKey("HistoryImportId");
            counts.HasKey(c => c.Id);
            counts.Property(c => c.Id).ValueGeneratedNever();

            counts.Property(c => c.BillingPeriodId)
                .HasConversion(id => id.Value, value => new BillingPeriodId(value))
                .IsRequired();

            counts.Property(c => c.UserId)
                .HasConversion(id => id.Value, value => new UserId(value))
                .IsRequired();

            counts.Property(c => c.Cups)
                .IsRequired();

            counts.HasIndex(c => new { c.BillingPeriodId, c.UserId }).IsUnique();
        });

        builder.OwnsMany(h => h.Purchases, purchases =>
        {
            purchases.ToTable("HistoricalPurchases");
            purchases.WithOwner().HasForeignKey("HistoryImportId");
            purchases.HasKey(p => p.Id);
            purchases.Property(p => p.Id).ValueGeneratedNever();

            purchases.Property(p => p.BillingPeriodId)
                .HasConversion(id => id.Value, value => new BillingPeriodId(value))
                .IsRequired();

            purchases.Property(p => p.PurchasedAt)
                .HasColumnType("date");

            purchases.Property(p => p.PaidByUserId)
                .HasConversion(new ValueConverter<UserId?, Guid?>(
                    id => id.HasValue ? id.Value.Value : (Guid?)null,
                    value => value.HasValue ? new UserId(value.Value) : (UserId?)null));

            purchases.Property(p => p.Description)
                .HasMaxLength(HistoricalPurchase.MaxDescriptionLength)
                .IsRequired();

            purchases.Property(p => p.Grams)
                .HasPrecision(18, 2);

            purchases.Property(p => p.Cost)
                .HasPrecision(18, 4)
                .IsRequired();

            purchases.Property(p => p.Currency)
                .HasMaxLength(3)
                .IsRequired();

            purchases.HasIndex(p => p.BillingPeriodId);
        });

        builder.Navigation(h => h.CupCounts).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(h => h.Purchases).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
