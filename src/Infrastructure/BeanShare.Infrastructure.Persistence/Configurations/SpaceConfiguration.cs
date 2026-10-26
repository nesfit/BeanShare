using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BeanShare.Infrastructure.Persistence.Configurations;

public sealed class SpaceConfiguration : IEntityTypeConfiguration<Space>
{
    public void Configure(EntityTypeBuilder<Space> builder)
    {
        builder.ToTable("Spaces");

        builder.HasKey(s => s.Id);
        
        builder.Property(s => s.Id)
            .HasConversion(
                spaceId => spaceId.Value,
                value => new SpaceId(value))
            .HasColumnName("Id");

        builder.Property(s => s.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(s => s.Currency)
            .HasConversion(
                currency => currency.Code,
                code => Currency.Create(code))
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(s => s.InviteCode)
            .HasConversion(
                code => code.Value,
                value => new InviteCode(value))
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(s => s.IsActive)
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.Property(s => s.RestockLeadTimeDays)
            .IsRequired()
            .HasDefaultValue(7);

        builder.Property(s => s.RestockMinOrderValue)
            .HasColumnType("decimal(18,2)")
            .IsRequired()
            .HasDefaultValue(2000m);

        builder.Property(s => s.TreasurerUserId)
            .HasConversion(new ValueConverter<UserId?, Guid?>(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : (UserId?)null));

        builder.Property(s => s.LedgerStartDate);

        builder.Property(s => s.LedgerVersion)
            .IsConcurrencyToken();

        builder.OwnsMany(s => s.Members, memberBuilder =>
        {
            memberBuilder.ToTable("SpaceMemberships");

            memberBuilder.WithOwner()
                .HasForeignKey("SpaceId");

            memberBuilder.HasKey("SpaceId", "UserId");

            memberBuilder.Property<SpaceId>("SpaceId")
                .HasConversion(
                    spaceId => spaceId.Value,
                    value => new SpaceId(value));

            memberBuilder.Property(m => m.UserId)
                .HasConversion(
                    userId => userId.Value,
                    value => new UserId(value))
                .HasColumnName("UserId");

            memberBuilder.Property(m => m.Role)
                .HasConversion<string>()
                .HasMaxLength(20);

            memberBuilder.Property(m => m.JoinedAt)
                .IsRequired();

            memberBuilder.HasIndex("UserId")
                .HasDatabaseName("IX_SpaceMemberships_UserId");
        });

        builder.Navigation(s => s.Members)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(s => s.DomainEvents);
    }
}