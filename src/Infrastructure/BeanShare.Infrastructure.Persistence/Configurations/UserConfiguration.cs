using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BeanShare.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id)
            .HasConversion(
                id => id.Value,
                value => new UserId(value))
            .HasColumnName("Id")
            .IsRequired();

        builder.Property(u => u.Email)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(u => u.Name)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(u => u.PictureUrl)
            .HasMaxLength(512)
            .IsRequired(false);

        builder.Property(u => u.Provider)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(u => u.ProviderUserId)
            .HasMaxLength(256)
            .IsRequired(false);

        builder.Property(u => u.PasswordHash)
            .HasMaxLength(512)
            .IsRequired(false);

        builder.Property(u => u.CreatedAt)
            .IsRequired();

        builder.Property(u => u.LastLoginAt)
            .IsRequired();

        builder.Property(u => u.PreferredCurrencyCode)
            .HasMaxLength(3)
            .IsRequired(false);

        builder.Property(u => u.RevolutTag)
            .HasMaxLength(30)
            .IsRequired(false);

        builder.Property(u => u.SystemRole)
            .HasConversion<int>()
            .HasDefaultValue(SystemRole.User)
            .IsRequired();

        builder.Property(u => u.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(u => u.DeactivatedAt)
            .IsRequired(false);

        builder.Property(u => u.IsPlaceholder)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(u => u.MergedIntoUserId)
            .HasConversion(new ValueConverter<UserId?, Guid?>(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : (UserId?)null));

        builder.HasIndex(u => u.SystemRole)
            .HasDatabaseName("IX_Users_SystemRole");

        builder.HasIndex(u => u.IsActive)
            .HasDatabaseName("IX_Users_IsActive");

        builder.HasIndex(u => new { u.Provider, u.ProviderUserId })
            .IsUnique()
            .HasDatabaseName("IX_Users_Provider_ProviderUserId");

        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName("IX_Users_Email");
    }
}
