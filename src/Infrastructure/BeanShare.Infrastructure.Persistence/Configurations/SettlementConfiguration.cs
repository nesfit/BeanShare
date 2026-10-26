using BeanShare.Domain.Aggregates.Settlement;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BeanShare.Infrastructure.Persistence.Configurations;

public sealed class SettlementConfiguration : IEntityTypeConfiguration<Settlement>
{
    private static readonly ValueConverter<SettlementId?, Guid?> NullableSettlementIdConverter = new(
        id => id.HasValue ? id.Value.Value : (Guid?)null,
        value => value.HasValue ? new SettlementId(value.Value) : (SettlementId?)null);

    public void Configure(EntityTypeBuilder<Settlement> builder)
    {
        builder.ToTable("Settlements");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasConversion(
                id => id.Value,
                value => new SettlementId(value))
            .HasColumnName("Id");

        builder.Property(s => s.SpaceId)
            .HasConversion(
                spaceId => spaceId.Value,
                value => new SpaceId(value))
            .HasColumnName("SpaceId")
            .IsRequired();

        builder.Property(s => s.BillingPeriodId)
            .HasConversion(
                id => id.Value,
                value => new BillingPeriodId(value))
            .HasColumnName("BillingPeriodId")
            .IsRequired();

        builder.Property(s => s.TotalAmount)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(s => s.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(s => s.GeneratedAt)
            .IsRequired();

        builder.Property(s => s.GeneratedBy)
            .HasConversion(
                userId => userId.Value,
                value => new UserId(value))
            .HasColumnName("GeneratedBy")
            .IsRequired();

        builder.Property(s => s.Status)
            .HasConversion(
                status => status.ToString(),
                value => Enum.Parse<SettlementStatus>(value))
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(s => s.CompletedAt);

        builder.Property(s => s.Model);

        builder.Property(s => s.Sequence);

        builder.Property(s => s.TreasurerUserId)
            .HasConversion(new ValueConverter<UserId?, Guid?>(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : (UserId?)null));

        builder.Property(s => s.SupersededBySettlementId)
            .HasConversion(NullableSettlementIdConverter);

        builder.Property(s => s.Version)
            .IsConcurrencyToken();

        builder.Ignore(s => s.AreAllLinesConfirmed);
        builder.Ignore(s => s.ConfirmedLinesCount);
        builder.Ignore(s => s.TotalLinesCount);
        builder.Ignore(s => s.IsLedger);

        builder.OwnsMany(s => s.Lines, linesConfig =>
        {
            linesConfig.ToTable("SettlementLines");

            linesConfig.WithOwner()
                .HasForeignKey("SettlementId");

            linesConfig.HasKey("SettlementId", "UserId");

            linesConfig.Property<SettlementId>("SettlementId")
                .HasConversion(
                    id => id.Value,
                    value => new SettlementId(value));

            linesConfig.Property(l => l.UserId)
                .HasConversion(
                    userId => userId.Value,
                    value => new UserId(value))
                .HasColumnName("UserId");

            linesConfig.Property(l => l.TotalCoffeeGrams)
                .HasPrecision(18, 2)
                .IsRequired();

            linesConfig.Property(l => l.TotalMilkMl)
                .HasPrecision(18, 2);

            linesConfig.OwnsOne(l => l.AmountDue, moneyBuilder =>
            {
                moneyBuilder.Property(m => m.Amount)
                    .HasColumnName("AmountDue")
                    .HasPrecision(18, 4)
                    .IsRequired();

                moneyBuilder.Property(m => m.Currency)
                    .HasConversion(
                        currency => currency.Code,
                        code => Currency.Create(code))
                    .HasColumnName("Currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

            linesConfig.Property(l => l.CreatedAt)
                .IsRequired();

            linesConfig.OwnsOne(l => l.Confirmation, confirmationBuilder =>
            {
                confirmationBuilder.Property(c => c.ConfirmedBy)
                    .HasConversion(
                        userId => userId.Value,
                        value => new UserId(value))
                    .HasColumnName("ConfirmedBy");

                confirmationBuilder.Property(c => c.ConfirmedAt)
                    .HasColumnName("ConfirmedAt");
            });

            linesConfig.Property(l => l.CreditAmount).HasPrecision(18, 4);
            linesConfig.Property(l => l.CarriedInAmount).HasPrecision(18, 4);
            linesConfig.Property(l => l.AdjustmentsAmount).HasPrecision(18, 4);
            linesConfig.Property(l => l.RoundingAmount).HasPrecision(18, 4);
            linesConfig.Property(l => l.NetAmount).HasPrecision(18, 4);
            linesConfig.Property(l => l.CarriedOutAmount).HasPrecision(18, 4);
            linesConfig.Property(l => l.LegacyAmountPaid).HasPrecision(18, 4);
            linesConfig.Property(l => l.Resolution);

            linesConfig.Property(l => l.CarriedFromSettlementId)
                .HasConversion(NullableSettlementIdConverter);

            linesConfig.Property(l => l.CarriedToSettlementId)
                .HasConversion(NullableSettlementIdConverter);

            // Each open line of a settlement is carried at most once (I6).
            linesConfig.HasIndex(nameof(SettlementLine.CarriedFromSettlementId), nameof(SettlementLine.UserId))
                .IsUnique()
                .HasFilter("\"CarriedFromSettlementId\" IS NOT NULL");

            linesConfig.Ignore(l => l.IsConfirmed);
            linesConfig.Ignore(l => l.IsLedger);
            linesConfig.Ignore(l => l.IsOpen);
            linesConfig.Ignore(l => l.IsCarried);
        });

        builder.Navigation(s => s.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(s => s.DomainEvents);

        builder.HasIndex(s => s.SpaceId);
        builder.HasIndex(s => s.BillingPeriodId).IsUnique();

        // Serializes generations within a space.
        builder.HasIndex(s => new { s.SpaceId, s.Sequence })
            .IsUnique()
            .HasFilter("\"Sequence\" IS NOT NULL");
    }
}