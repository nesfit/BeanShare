using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Application.Features.Consumption.Commands;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace BeanShare.Infrastructure.Tests.InMemory;

// Saves that replace owned value objects still pass the Version token check and bump it.
[Collection(InMemoryContainerCollection.Name)]
public sealed class OwnedTypeTokenTests
{
    [Fact(Timeout = 10000)]
    public async Task UpdateConsumption_WithANewOwnedProduct_SavesAndBumpsTheCupVersion()
    {
        await using var container = await InMemoryContainer.CreateAsync();
        var scenario = await LedgerScenario.CreateAsync(container);
        var cupId = await scenario.LogCupAsync(LedgerScenario.Today.AddDays(-1));
        var before = await CupVersionAsync(container, cupId);

        var result = await container.SendAsync(scenario.Admin, new UpdateConsumptionCommand(
            cupId, scenario.SpaceId, LedgerScenario.ProductName, LedgerScenario.ProductBrand, "Filter", 12m,
            LedgerScenario.Today.AddDays(-1)));

        result.IsSuccess.Should().BeTrue();
        (await CupVersionAsync(container, cupId)).Should().BeGreaterThan(before);
        var grams = await container.ReadAsync(db => db.Consumptions.AsNoTracking()
            .Where(c => c.Id == new ConsumptionEntryId(cupId)).Select(c => c.Quantity).SingleAsync());
        grams.Grams.Should().Be(12m);
    }

    [Fact(Timeout = 10000)]
    public async Task UpdateStockPurchase_ReplacingQuantityAndCost_SavesAndBumpsTheBagVersion()
    {
        await using var container = await InMemoryContainer.CreateAsync();
        var scenario = await LedgerScenario.CreateAsync(container);
        var bag = await SingleBagAsync(container);

        var result = await container.SendAsync(scenario.Admin, new UpdateStockPurchaseCommand(
            scenario.SpaceId, bag.Id, 900m, 25m, bag.PurchasedAt));

        result.IsSuccess.Should().BeTrue();
        var reloaded = await SingleBagAsync(container);
        reloaded.Version.Should().BeGreaterThan(bag.Version);
        reloaded.Quantity.Grams.Should().Be(900m);
        reloaded.Cost.Amount.Should().Be(25m);
    }

    [Fact(Timeout = 10000)]
    public async Task UpdateStockPurchase_ReplacingTheProduct_SavesAndBumpsTheBagVersion()
    {
        await using var container = await InMemoryContainer.CreateAsync();
        var scenario = await LedgerScenario.CreateAsync(container);
        var bag = await SingleBagAsync(container);

        var result = await container.SendAsync(scenario.Admin, new UpdateStockPurchaseCommand(
            scenario.SpaceId, bag.Id, 1000m, 20m, bag.PurchasedAt, ProductName: "Beta"));

        result.IsSuccess.Should().BeTrue();
        var reloaded = await SingleBagAsync(container);
        reloaded.Version.Should().BeGreaterThan(bag.Version);
        reloaded.Product.Name.Should().Be("Beta");
    }

    [Fact(Timeout = 10000)]
    public async Task AddStockPurchase_WithThreeBags_PersistsEachClonedLotAndTheirTokensWork()
    {
        await using var container = await InMemoryContainer.CreateAsync();
        var scenario = await LedgerScenario.CreateAsync(container);

        var result = await container.SendAsync(scenario.Admin, new AddStockPurchaseCommand(
            scenario.SpaceId, "Gamma", "Roaster", "Filter", 250m, 8m, "USD", "Store", LedgerScenario.Today.AddDays(-2), BagCount: 3));
        result.IsSuccess.Should().BeTrue();

        var lots = await container.ReadAsync(db => db.CoffeeStocks.AsNoTracking().Include(s => s.Purchases)
            .SelectMany(s => s.Purchases).Where(p => p.Product.Name == "Gamma").ToListAsync());
        lots.Should().HaveCount(3);
        lots.Should().OnlyContain(p => p.Quantity.Grams == 250m && p.Cost.Amount == 8m && p.Product.Brand == "Roaster");

        var cup = await container.SendAsync(scenario.Admin, new RecordConsumptionCommand(
            scenario.SpaceId, "Gamma", "Roaster", "Filter", 10m, LedgerScenario.Today, StockLotId: lots[1].Id));
        cup.IsSuccess.Should().BeTrue();
        var drawn = await container.ReadAsync(db => db.CoffeeStocks.AsNoTracking().Include(s => s.Purchases)
            .SelectMany(s => s.Purchases).SingleAsync(p => p.Id == lots[1].Id));
        drawn.Version.Should().BeGreaterThan(lots[1].Version);
    }

    private static Task<int> CupVersionAsync(InMemoryContainer container, Guid cupId) =>
        container.ReadAsync(db => db.Consumptions.AsNoTracking()
            .Where(c => c.Id == new ConsumptionEntryId(cupId)).Select(c => c.Version).SingleAsync());

    private static Task<Purchase> SingleBagAsync(InMemoryContainer container) =>
        container.ReadAsync(db => db.CoffeeStocks.AsNoTracking().Include(s => s.Purchases)
            .SelectMany(s => s.Purchases).SingleAsync(p => p.Product.Name != "Gamma"));
}
