using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Aggregates.Settlement;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Infrastructure.Persistence.Configurations;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BeanShare.Infrastructure.Persistence;

public sealed class BeanShareDbContext(
    DbContextOptions<BeanShareDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Space> Spaces => Set<Space>();
    public DbSet<CoffeeStock> CoffeeStocks => Set<CoffeeStock>();
    public DbSet<ConsumptionEntry> Consumptions => Set<ConsumptionEntry>();
    public DbSet<BillingPeriod> BillingPeriods => Set<BillingPeriod>();
    public DbSet<Settlement> Settlements => Set<Settlement>();
    public DbSet<PresetRecipe> PresetRecipes => Set<PresetRecipe>();
    public DbSet<GlobalPreset> GlobalPresets => Set<GlobalPreset>();
    public DbSet<SpaceGlobalPresetConfig> SpaceGlobalPresetConfigs => Set<SpaceGlobalPresetConfig>();
    public DbSet<UserPresetFavorite> UserPresetFavorites => Set<UserPresetFavorite>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ActiveQrCode> ActiveQrCodes => Set<ActiveQrCode>();

    /// <summary>
    /// Collects domain events from tracked aggregates and clears them.
    /// Called by UnitOfWorkBehavior before SaveChanges so events can be dispatched after commit.
    /// </summary>
    public IReadOnlyList<IDomainEvent> CollectDomainEvents()
    {
        var aggregatesWithEvents = ChangeTracker.Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        var events = aggregatesWithEvents
            .SelectMany(a => a.DomainEvents)
            .ToList();

        foreach (var aggregate in aggregatesWithEvents)
            aggregate.ClearDomainEvents();

        return events;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new SpaceConfiguration());
        modelBuilder.ApplyConfiguration(new CoffeeStockConfiguration());
        modelBuilder.ApplyConfiguration(new PurchaseConfiguration());
        modelBuilder.ApplyConfiguration(new ConsumptionEntryConfiguration());
        modelBuilder.ApplyConfiguration(new BillingPeriodConfiguration());
        modelBuilder.ApplyConfiguration(new SettlementConfiguration());
        modelBuilder.ApplyConfiguration(new PresetRecipeConfiguration());
        modelBuilder.ApplyConfiguration(new GlobalPresetConfiguration());
        modelBuilder.ApplyConfiguration(new SpaceGlobalPresetConfigConfiguration());
        modelBuilder.ApplyConfiguration(new UserPresetFavoriteConfiguration());
        modelBuilder.ApplyConfiguration(new ExchangeRateConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationConfiguration());
        modelBuilder.ApplyConfiguration(new ActiveQrCodeConfiguration());
    }
}