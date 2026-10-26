using BeanShare.Application.Abstractions;
using BeanShare.Application.Services;
using BeanShare.Infrastructure.Persistence.Repositories;
using BeanShare.Infrastructure.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BeanShare.Infrastructure.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString, bool useInMemory = false)
    {
        if (useInMemory)
        {
            // Use in-memory database for testing
            services.AddDbContext<BeanShareDbContext>(options =>
                options.UseInMemoryDatabase("BeanShareInMemory")
                       .EnableSensitiveDataLogging()
                       .EnableDetailedErrors()
                       .ConfigureWarnings(warnings =>
                           warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        }
        else
        {
            // Use PostgreSQL for production
            services.AddDbContext<BeanShareDbContext>(options =>
                options.UseNpgsql(connectionString, npgsqlOptions =>
                    npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
        }

        services.AddScoped<ISpaceRepository, SpaceRepository>();
        services.AddScoped<ICoffeeStockRepository, CoffeeStockRepository>();
        services.AddScoped<IConsumptionRepository, ConsumptionRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IBillingPeriodRepository, BillingPeriodRepository>();
        services.AddScoped<ISettlementRepository, SettlementRepository>();
        services.AddScoped<IPresetRecipeRepository, PresetRecipeRepository>();
        services.AddScoped<IGlobalPresetRepository, GlobalPresetRepository>();
        services.AddScoped<ISpaceGlobalPresetConfigRepository, SpaceGlobalPresetConfigRepository>();
        services.AddScoped<IUserPresetFavoriteRepository, UserPresetFavoriteRepository>();
        services.AddScoped<IExchangeRateRepository, ExchangeRateRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IActiveQrCodeRepository, ActiveQrCodeRepository>();
        services.AddScoped<IBalanceEntryRepository, BalanceEntryRepository>();
        services.AddScoped<IInviteCodeGenerator, InviteCodeGenerator>();
        services.AddScoped<ICostCalculationService, CostCalculationService>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        return services;
    }
}