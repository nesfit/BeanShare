using BeanShare.Application.Features.Billing.Commands.CreateBillingPeriod;
using BeanShare.Application.Features.Billing.Commands.OpenBillingPeriod;
using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Application.Features.Consumption.Commands;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Domain.Common;

namespace BeanShare.Infrastructure.Tests.InMemory;

/// <summary>A space with one admin, one 1000 g bag and helpers to log cups and open periods.</summary>
internal sealed class LedgerScenario
{
    public const string ProductName = "Alfa";
    public const string ProductBrand = "Roaster";

    private LedgerScenario(InMemoryContainer container, UserId admin, Guid spaceId)
    {
        Container = container;
        Admin = admin;
        SpaceId = spaceId;
    }

    public InMemoryContainer Container { get; }
    public UserId Admin { get; }
    public Guid SpaceId { get; }

    public static DateTime Today => DateTime.UtcNow.Date;

    public static async Task<LedgerScenario> CreateAsync(InMemoryContainer container, string currency = "USD")
    {
        var admin = await container.SeedUserAsync("admin@example.org", "Admin");
        var created = await container.SendAsync(admin, new CreateSpaceCommand("Kava", currency));
        created.IsSuccess.Should().BeTrue();

        var scenario = new LedgerScenario(container, admin, created.Value.SpaceId);
        var bag = await container.SendAsync(admin, new AddStockPurchaseCommand(
            scenario.SpaceId, ProductName, ProductBrand, "Filter", 1000m, 20m, currency, "Store", Today.AddDays(-20)));
        bag.IsSuccess.Should().BeTrue();
        return scenario;
    }

    public async Task<Guid> LogCupAsync(DateTime consumedAt, decimal grams = 10m)
    {
        var cup = await Container.SendAsync(Admin, new RecordConsumptionCommand(
            SpaceId, ProductName, ProductBrand, "Filter", grams, consumedAt));
        cup.IsSuccess.Should().BeTrue();
        return cup.Value.Id;
    }

    public async Task<Guid> CreateOpenPeriodAsync(DateTime start, DateTime end)
    {
        var period = await Container.SendAsync(Admin, new CreateBillingPeriodCommand(SpaceId, "P", start, end));
        period.IsSuccess.Should().BeTrue();
        (await Container.SendAsync(Admin, new OpenBillingPeriodCommand(period.Value.Id))).IsSuccess.Should().BeTrue();
        return period.Value.Id;
    }
}
