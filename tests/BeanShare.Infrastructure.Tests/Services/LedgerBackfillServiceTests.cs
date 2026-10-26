using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.ValueObjects;
using BeanShare.Infrastructure.Persistence;
using BeanShare.Infrastructure.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SettlementAggregate = BeanShare.Domain.Aggregates.Settlement.Settlement;

namespace BeanShare.Infrastructure.Tests.Services;

// T-INF-03: the backfill stamps what legacy settlements already billed, freezes legacy Paid, and is idempotent.
public sealed class LedgerBackfillServiceTests
{
    private readonly DbContextOptions<BeanShareDbContext> _options = new DbContextOptionsBuilder<BeanShareDbContext>()
        .UseInMemoryDatabase($"backfill-{Guid.NewGuid()}")
        .Options;

    private readonly SettableClock _clock = new(new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc));
    private readonly SpaceId _spaceId = SpaceId.New();
    private readonly UserId _admin = UserId.New();
    private readonly UserId _member = UserId.New();

    [Fact]
    public async Task Run_StampsBilledCupsFreezesLegacyPaidAndIsIdempotent()
    {
        var january = Period("January", new DateTime(2026, 1, 1), new DateTime(2026, 1, 15));
        var settledWithoutSettlement = Period("Second half", new DateTime(2026, 1, 16), new DateTime(2026, 1, 31));

        _clock.Set(new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc));
        var billedByLegacy = Cup(_member, new DateTime(2026, 1, 10, 9, 0, 0, DateTimeKind.Utc));
        var billedBySettledPeriod = Cup(_member, new DateTime(2026, 1, 18, 9, 0, 0, DateTimeKind.Utc));
        var stock = CoffeeStock.Create(_spaceId, _clock);
        stock.AddPurchase(CoffeeProduct.Create("Alfa", "R", CoffeeType.Filter), Weight.FromGrams(1000), Money.Create(400m, "CZK"),
            "S", _admin, new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), _clock);

        _clock.Set(new DateTime(2026, 1, 21, 0, 0, 0, DateTimeKind.Utc));
        var legacy = SettlementAggregate.Create(_spaceId, january.Id, "CZK", _admin, _clock);
        legacy.AddLine(_member, 10m, 4m, _clock);
        legacy.AddLine(_admin, 0m, 0m, _clock);
        legacy.FinalizeGeneration(_clock);
        settledWithoutSettlement.Close(_admin, _clock);
        settledWithoutSettlement.MarkAsSettled(_admin, _clock);

        _clock.Set(new DateTime(2026, 1, 28, 0, 0, 0, DateTimeKind.Utc));
        var backDatedAfterGeneration = Cup(_member, new DateTime(2026, 1, 11, 9, 0, 0, DateTimeKind.Utc));
        var loggedAfterSettling = Cup(_member, new DateTime(2026, 1, 19, 9, 0, 0, DateTimeKind.Utc));

        await using (var db = new BeanShareDbContext(_options))
        {
            db.BillingPeriods.AddRange(january, settledWithoutSettlement);
            db.Settlements.Add(legacy);
            db.CoffeeStocks.Add(stock);
            db.Consumptions.AddRange(billedByLegacy, billedBySettledPeriod, backDatedAfterGeneration, loggedAfterSettling);
            await db.SaveChangesAsync();
        }

        await RunBackfillAsync();
        var firstRun = await SnapshotAsync();
        await RunBackfillAsync();
        var secondRun = await SnapshotAsync();

        firstRun.Stamps[billedByLegacy.Id].Should().Be(january.Id);
        firstRun.Stamps[backDatedAfterGeneration.Id].Should().BeNull("it was logged after the legacy settlement, so the ledger bills it");
        firstRun.Stamps[billedBySettledPeriod.Id].Should().Be(settledWithoutSettlement.Id);
        firstRun.Stamps[loggedAfterSettling.Id].Should().BeNull();
        firstRun.LegacyPaid[_admin].Should().Be(400m);
        firstRun.LegacyPaid[_member].Should().Be(0m);
        secondRun.Should().BeEquivalentTo(firstRun);
    }

    // Rev8 (B26): imported history has no settlement by design, and step 2 must not give it live cups.
    [Fact]
    public async Task Run_SkipsAHistoricalSettledPeriodWithoutASettlement()
    {
        _clock.Set(new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc));
        var cup = Cup(_member, new DateTime(2026, 1, 10, 9, 0, 0, DateTimeKind.Utc));
        _clock.Set(new DateTime(2026, 1, 25, 0, 0, 0, DateTimeKind.Utc));
        var historical = BillingPeriod.CreateHistorical(_spaceId, "Imported January", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc), HistoryImportId.New(), _admin, _clock);

        await using (var db = new BeanShareDbContext(_options))
        {
            db.BillingPeriods.Add(historical);
            db.Consumptions.Add(cup);
            await db.SaveChangesAsync();
        }

        await RunBackfillAsync();

        await using var check = new BeanShareDbContext(_options);
        (await check.Consumptions.AsNoTracking().SingleAsync()).BillingPeriodId.Should().BeNull();
    }


    private async Task RunBackfillAsync()
    {
        await using var db = new BeanShareDbContext(_options);
        await new LedgerBackfillService(db, NullLogger<LedgerBackfillService>.Instance).RunAsync();
    }

    private async Task<(Dictionary<ConsumptionEntryId, BillingPeriodId?> Stamps, Dictionary<UserId, decimal?> LegacyPaid)> SnapshotAsync()
    {
        await using var db = new BeanShareDbContext(_options);
        var stamps = await db.Consumptions.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.BillingPeriodId);
        var settlement = await db.Settlements.AsNoTracking().Include(s => s.Lines).SingleAsync();
        return (stamps, settlement.Lines.ToDictionary(l => l.UserId, l => l.LegacyAmountPaid));
    }

    private BillingPeriod Period(string name, DateTime start, DateTime end)
    {
        _clock.Set(new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc));
        var period = BillingPeriod.Create(_spaceId, name, DateTime.SpecifyKind(start, DateTimeKind.Utc), DateTime.SpecifyKind(end, DateTimeKind.Utc), _admin, _clock);
        period.Open(_admin, _clock);
        if (name == "January")
        {
            period.Close(_admin, _clock);
            period.MarkAsSettled(_admin, _clock);
        }
        return period;
    }

    private ConsumptionEntry Cup(UserId user, DateTime consumedAt) =>
        ConsumptionEntry.Create(_spaceId, user, CoffeeProduct.Create("Alfa", "R", CoffeeType.Filter), Weight.FromGrams(10), consumedAt, _clock);

    private sealed class SettableClock(DateTime start) : IClock
    {
        public DateTime UtcNow { get; private set; } = start;
        public void Set(DateTime now) => UtcNow = now;
    }
}
