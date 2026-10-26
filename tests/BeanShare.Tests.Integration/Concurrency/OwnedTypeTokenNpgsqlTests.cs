using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Application.Features.Consumption.Commands;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Domain.ValueObjects;
using BeanShare.Tests.Integration.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BeanShare.Tests.Integration.Concurrency;

// T-INF-08 (Npgsql variant): replacing owned value objects (table-split columns) still passes the
// owner's Version token and bumps it.
public sealed class OwnedTypeTokenNpgsqlTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private NpgsqlAppContainer _container = null!;

    public OwnedTypeTokenNpgsqlTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _container = NpgsqlAppContainer.Create(_fixture.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task OwnedReplacements_SaveWithTheTokenAndBumpIt()
    {
        var admin = await _container.SeedUserAsync("Admin");
        var spaceId = (await _container.SendAsync(admin, new CreateSpaceCommand("Owned", "USD"))).Value.SpaceId;
        (await _container.SendAsync(admin, new AddStockPurchaseCommand(
            spaceId, "Alfa", "Roaster", "Filter", 250m, 8m, "USD", "Store", DateTime.UtcNow.Date.AddDays(-2), BagCount: 3)))
            .IsSuccess.Should().BeTrue();

        var lots = await LotsAsync(spaceId);
        lots.Should().HaveCount(3);
        lots.Should().OnlyContain(p => p.Quantity.Grams == 250m && p.Cost.Amount == 8m && p.Product.Name == "Alfa");

        var consumedAt = DateTime.UtcNow.AddMinutes(-5);
        var cupId = (await _container.SendAsync(admin, new RecordConsumptionCommand(
            spaceId, "Alfa", "Roaster", "Filter", 10m, consumedAt))).Value.Id;
        var cupVersion = await CupVersionAsync(cupId);

        (await _container.SendAsync(admin, new UpdateConsumptionCommand(cupId, spaceId, "Alfa", "Roaster", "Filter", 12m, consumedAt)))
            .IsSuccess.Should().BeTrue();
        (await CupVersionAsync(cupId)).Should().BeGreaterThan(cupVersion);

        var bag = (await LotsAsync(spaceId)).OrderBy(p => p.Label).Last();
        (await _container.SendAsync(admin, new UpdateStockPurchaseCommand(spaceId, bag.Id, 240m, 9m, bag.PurchasedAt, ProductName: "Beta")))
            .IsSuccess.Should().BeTrue();

        var updated = (await LotsAsync(spaceId)).Single(p => p.Id == bag.Id);
        updated.Version.Should().BeGreaterThan(bag.Version);
        updated.Quantity.Grams.Should().Be(240m);
        updated.Cost.Amount.Should().Be(9m);
        updated.Product.Name.Should().Be("Beta");
    }

    private Task<List<Domain.Aggregates.CoffeeStock.Purchase>> LotsAsync(Guid spaceId) =>
        _container.ReadAsync(db => db.CoffeeStocks.AsNoTracking().Include(s => s.Purchases)
            .Where(s => s.SpaceId == new SpaceId(spaceId)).SelectMany(s => s.Purchases).ToListAsync());

    private Task<int> CupVersionAsync(Guid cupId) =>
        _container.ReadAsync(db => db.Consumptions.AsNoTracking()
            .Where(c => c.Id == new ConsumptionEntryId(cupId)).Select(c => c.Version).SingleAsync());
}
