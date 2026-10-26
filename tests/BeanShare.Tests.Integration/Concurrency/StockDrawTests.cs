using BeanShare.Application.Common;
using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Application.Features.Consumption.Commands;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Domain.Common;
using BeanShare.Domain.ValueObjects;
using BeanShare.Tests.Integration.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BeanShare.Tests.Integration.Concurrency;

// T-INT-10 (Npgsql only): racing cups on one bag both land through ConcurrencyRetryBehavior;
// two edits of the same cup never become a silent last-writer-wins.
public sealed class StockDrawTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private NpgsqlAppContainer _container = null!;

    public StockDrawTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _container = NpgsqlAppContainer.Create(_fixture.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private async Task<(UserId Admin, Guid SpaceId)> SpaceWithOneBagAsync()
    {
        var admin = await _container.SeedUserAsync("Admin");
        var spaceId = (await _container.SendAsync(admin, new CreateSpaceCommand("Draws", "USD"))).Value.SpaceId;
        var bag = await _container.SendAsync(admin, new AddStockPurchaseCommand(
            spaceId, "Alfa", "Roaster", "Filter", 1000m, 20m, "USD", "Store", DateTime.UtcNow.Date.AddDays(-3)));
        bag.IsSuccess.Should().BeTrue();
        return (admin, spaceId);
    }

    [Fact]
    public async Task TwoCupsFromTheSameBag_AreBothRecordedWithCorrectCounters()
    {
        var (admin, spaceId) = await SpaceWithOneBagAsync();
        var cup = new RecordConsumptionCommand(spaceId, "Alfa", "Roaster", "Filter", 10m, DateTime.UtcNow.AddMinutes(-1));

        _container.Barrier.Arm();
        var held = Task.Run(() => _container.SendAsync(admin, cup));
        await _container.Barrier.WaitUntilHeldAsync();

        (await _container.SendAsync(admin, cup)).IsSuccess.Should().BeTrue();

        _container.Barrier.Release();
        (await held).IsSuccess.Should().BeTrue("the bag-counter conflict is retried in a fresh change tracker");

        var consumed = await _container.ReadAsync(db => db.CoffeeStocks.AsNoTracking().Include(s => s.Purchases)
            .Where(s => s.SpaceId == new SpaceId(spaceId)).SelectMany(s => s.Purchases).Select(p => p.ConsumedGrams).SingleAsync());
        var cups = await _container.ReadAsync(db => db.Consumptions.AsNoTracking().CountAsync(c => c.SpaceId == new SpaceId(spaceId)));
        consumed.Should().Be(20m);
        cups.Should().Be(2);
    }

    [Fact]
    public async Task TwoEditsOfTheSameCup_TheHeldOneGets409WithoutRetry()
    {
        var (admin, spaceId) = await SpaceWithOneBagAsync();
        var cupId = (await _container.SendAsync(admin, new RecordConsumptionCommand(
            spaceId, "Alfa", "Roaster", "Filter", 10m, DateTime.UtcNow.AddMinutes(-5)))).Value.Id;
        var consumedAt = DateTime.UtcNow.AddMinutes(-5);

        _container.Barrier.Arm();
        var held = Task.Run(() => _container.SendAsync(admin,
            new UpdateConsumptionCommand(cupId, spaceId, "Alfa", "Roaster", "Filter", 12m, consumedAt)));
        await _container.Barrier.WaitUntilHeldAsync();

        (await _container.SendAsync(admin,
            new UpdateConsumptionCommand(cupId, spaceId, "Alfa", "Roaster", "Filter", 15m, consumedAt))).IsSuccess.Should().BeTrue();

        _container.Barrier.Release();
        var loser = await held;

        loser.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
        var grams = await _container.ReadAsync(db => db.Consumptions.AsNoTracking()
            .Where(c => c.Id == new ConsumptionEntryId(cupId)).Select(c => c.Quantity).SingleAsync());
        grams.Grams.Should().Be(15m, "the first committed edit is kept, not silently overwritten");
    }
}
