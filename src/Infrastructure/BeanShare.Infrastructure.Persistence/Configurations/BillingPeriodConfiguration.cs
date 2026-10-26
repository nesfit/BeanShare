using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Common;
using BeanShare.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BeanShare.Infrastructure.Persistence.Configurations;

public sealed class BillingPeriodConfiguration : IEntityTypeConfiguration<BillingPeriod>
{
    public void Configure(EntityTypeBuilder<BillingPeriod> builder)
    {
        builder.ToTable("BillingPeriods");

        builder.HasKey(bp => bp.Id);

        builder.Property(bp => bp.Id)
            .HasConversion(
                id => id.Value,
                value => new BillingPeriodId(value))
            .HasColumnName("Id");

        builder.Property(bp => bp.SpaceId)
            .HasConversion(
                spaceId => spaceId.Value,
                value => new SpaceId(value))
            .HasColumnName("SpaceId")
            .IsRequired();

        builder.Property(bp => bp.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(bp => bp.StartDate)
            .IsRequired();

        builder.Property(bp => bp.EndDate)
            .IsRequired();

        builder.Property(bp => bp.State)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(bp => bp.CreatedAt)
            .IsRequired();

        builder.Property(bp => bp.CreatedBy)
            .HasConversion(
                userId => userId.Value,
                value => new UserId(value))
            .HasColumnName("CreatedBy")
            .IsRequired();

        builder.Property(bp => bp.ClosedAt);

        builder.Property(bp => bp.ClosedBy)
            .HasConversion(
                userId => userId == null ? (Guid?)null : userId.Value,
                value => value.HasValue ? new UserId(value.Value) : null)
            .HasColumnName("ClosedBy");

        builder.Property(bp => bp.SettledAt);

        builder.Property(bp => bp.SettledBy)
            .HasConversion(
                userId => userId == null ? (Guid?)null : userId.Value,
                value => value.HasValue ? new UserId(value.Value) : null)
            .HasColumnName("SettledBy");

        builder.Property(bp => bp.Version)
            .IsConcurrencyToken();

        builder.Ignore(bp => bp.DomainEvents);

        builder.HasIndex(bp => bp.SpaceId);

        builder.HasIndex(bp => new { bp.SpaceId, bp.StartDate, bp.EndDate });
    }
}