using BeanShare.Application.Abstractions;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Services;
using BeanShare.Infrastructure.Communication;
using BeanShare.Infrastructure.Persistence;
using BeanShare.Infrastructure.Services;
using BeanShare.Infrastructure.Services.Documents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BeanShare.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString, bool useInMemoryDatabase = false)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICostingPolicy, WeightedAverageCostingPolicy>();

        services.AddPersistence(connectionString, useInMemoryDatabase);

        services.AddScoped<IUserService, UserService>();
        services.AddScoped<AvatarStorageService>();
        services.AddMemoryCache();

        services.AddSingleton<ISettlementReportGenerator, SettlementReportGenerator>();
        services.AddSingleton<ISettlementPdfGenerator>(sp =>
            new SettlementPdfGeneratorAdapter(sp.GetRequiredService<ISettlementReportGenerator>()));

        // Open Food Facts product lookup (no API key needed). Best-effort barcode → product metadata.
        services.AddHttpClient<IProductCatalogService, OpenFoodFactsProvider>(client =>
        {
            client.BaseAddress = new Uri("https://world.openfoodfacts.org/");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent", "BeanShare/1.0 (+https://beanshare.dev)");
            client.Timeout = TimeSpan.FromSeconds(6);
        });

        // services.AddIdentity();

        return services;
    }

    public static IServiceCollection AddCommunicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCommunication(configuration);
        return services;
    }

    public static IServiceCollection AddExchangeRates(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OpenExchangeRatesOptions>(
            configuration.GetSection(OpenExchangeRatesOptions.SectionName));

        services.AddHttpClient<IExchangeRateProvider, OpenExchangeRatesProvider>(client =>
        {
            client.BaseAddress = new Uri("https://openexchangerates.org/");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        services.AddScoped<ICurrencyConversionService, CurrencyConversionService>();

        services.AddHostedService<ExchangeRateUpdateService>();

        return services;
    }
}
