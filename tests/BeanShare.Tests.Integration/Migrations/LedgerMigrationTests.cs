using BeanShare.Application.Features.Settlement.Commands.GenerateSettlement;
using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.ValueObjects;
using BeanShare.Infrastructure.Persistence;
using BeanShare.Infrastructure.Persistence.Services;
using BeanShare.Tests.Integration.Concurrency;
using BeanShare.Tests.Integration.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SettlementAggregate = BeanShare.Domain.Aggregates.Settlement.Settlement;

namespace BeanShare.Tests.Integration.Migrations;

// After the migration and the backfill, the first ledger settlement bills only
// the cup a legacy settlement could not have covered.
public sealed class LedgerMigrationTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private NpgsqlAppContainer _container = null!;

    public LedgerMigrationTests(PostgreSqlFixture fixture)
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
    public async Task TheFirstLedgerSettlement_IncludesOnlyTheCupLoggedAfterTheLegacySettlement()
    {
        var s = await LedgerScenario.CreateAsync(_container);
        var today = LedgerScenario.Today;
        var legacyStart = today.AddDays(-40);
        var legacyEnd = today.AddDays(-21);
        var clock = new FixedClock(today.AddDays(-20));

        // Legacy state: a settled period, its legacy settlement, and two unstamped cups in its window —
        // one logged before the settlement was generated, one back-dated after.
        var legacyPeriod = BillingPeriod.Create(new SpaceId(s.SpaceId), "Legacy", legacyStart, legacyEnd, s.Admin, new FixedClock(legacyStart));
        legacyPeriod.Open(s.Admin, clock);
        legacyPeriod.Close(s.Admin, clock);
        legacyPeriod.MarkAsSettled(s.Admin, clock);
        var billed = Cup(s, s.Alice, legacyStart.AddDays(3), new FixedClock(legacyEnd));
        var legacy = SettlementAggregate.Create(new SpaceId(s.SpaceId), legacyPeriod.Id, "CZK", s.Admin, clock);
        legacy.AddLine(s.Alice, 10m, 5m, clock);
        legacy.FinalizeGeneration(clock);
        var backDated = Cup(s, s.Bob, legacyStart.AddDays(4), new FixedClock(today.AddDays(-10)));

        await using (var scope = _container.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BeanShareDbContext>();
            db.BillingPeriods.Add(legacyPeriod);
            db.Settlements.Add(legacy);
            db.Consumptions.AddRange(billed, backDated);
            await db.SaveChangesAsync();
            await new LedgerBackfillService(db, NullLogger<LedgerBackfillService>.Instance).RunAsync();
        }

        var next = await s.ClosedPeriodAsync(today.AddDays(-20), today.AddDays(-1));
        var result = await _container.SendAsync(s.Admin, new GenerateSettlementCommand(next));

        result.IsSuccess.Should().BeTrue();
        result.Value.Lines.Should().NotContain(l => l.UserId == s.Alice.Value, "the legacy settlement already billed Alice's cup");
        result.Value.Lines.Single(l => l.UserId == s.Bob.Value).TotalCoffeeGrams.Should().Be(10m);
        var legacyLine = await _container.ReadAsync(db => db.Settlements.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.Id == legacy.Id).SelectMany(x => x.Lines).SingleAsync());
        legacyLine.LegacyAmountPaid.Should().NotBeNull();
    }

    private static ConsumptionEntry Cup(LedgerScenario s, UserId user, DateTime consumedAt, IClock createdBy) =>
        ConsumptionEntry.Create(new SpaceId(s.SpaceId), user, CoffeeProduct.Create("Alfa", "Roaster", CoffeeType.Filter),
            Weight.FromGrams(10m), consumedAt, createdBy);

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow { get; } = now;
    }
}
