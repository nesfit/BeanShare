using BeanShare.Domain.Aggregates.Ledger;
using BeanShare.Domain.Common;
using BeanShare.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BeanShare.Infrastructure.Persistence.Configurations;

public sealed class BalanceEntryConfiguration : IEntityTypeConfiguration<BalanceEntry>
{
    public void Configure(EntityTypeBuilder<BalanceEntry> builder)
    {
        builder.ToTable("BalanceEntries", table =>
        {
            table.HasCheckConstraint("CK_BalanceEntries_Amount_NonZero", "\"Amount\" <> 0");
            table.HasCheckConstraint("CK_BalanceEntries_Parties_Differ", "\"UserId\" <> \"CounterpartyUserId\"");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new BalanceEntryId(value))
            .ValueGeneratedNever();

        builder.Property(e => e.SpaceId)
            .HasConversion(id => id.Value, value => new SpaceId(value))
            .IsRequired();

        builder.Property(e => e.UserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(e => e.CounterpartyUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(e => e.Amount)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(e => e.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(e => e.EffectiveDate)
            .IsRequired();

        builder.Property(e => e.Kind)
            .IsRequired();

        builder.Property(e => e.Note)
            .HasMaxLength(BalanceEntry.MaxNoteLength)
            .IsRequired();

        builder.Property(e => e.CreatedBy)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedBy)
            .HasConversion(new ValueConverter<UserId?, Guid?>(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : (UserId?)null));

        builder.Property(e => e.UpdatedAt);

        builder.Property(e => e.IncludedInSettlementId)
            .HasConversion(new ValueConverter<SettlementId?, Guid?>(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new SettlementId(value.Value) : (SettlementId?)null));

        builder.Property(e => e.Version)
            .IsConcurrencyToken();

        builder.Ignore(e => e.IsLocked);
        builder.Ignore(e => e.DomainEvents);

        builder.HasIndex(e => new { e.SpaceId, e.IncludedInSettlementId });
        builder.HasIndex(e => new { e.SpaceId, e.UserId });
        builder.HasIndex(e => new { e.SpaceId, e.CounterpartyUserId });
    }
}
