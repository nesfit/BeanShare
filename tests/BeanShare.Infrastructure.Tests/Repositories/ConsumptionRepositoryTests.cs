using BeanShare.Application.Abstractions;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using BeanShare.Infrastructure.Persistence;
using BeanShare.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeanShare.Infrastructure.Tests.Repositories;

public sealed class ConsumptionRepositoryTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _databaseFixture;

    public ConsumptionRepositoryTests(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
    }

    /// <summary>
    /// <see cref="LedgerConsumptionsSpecification"/> compares a value-converted <c>BillingPeriodId?</c>
    /// and optional date bounds; both have to survive translation to SQL, so this runs against Npgsql.
    /// </summary>
    [Fact]
    public async Task LedgerSpec_ForAPeriod_TakesItsStampsAndUnstampedCupsUpToItsEnd()
    {
        var clock = new TestClock();
        var spaceId = new SpaceId(Guid.NewGuid());
        var userId = new UserId(Guid.NewGuid());
        var periodId = BillingPeriodId.New();
        var ledgerStart = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc);

        var stampedAfterEnd = CreateCup(spaceId, userId, new DateTime(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc), clock);
        stampedAfterEnd.AssignToBillingPeriod(periodId);
        var unstampedInWindow = CreateCup(spaceId, userId, new DateTime(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc), clock);
        var unstampedAfterEnd = CreateCup(spaceId, userId, new DateTime(2026, 7, 5, 9, 0, 0, DateTimeKind.Utc), clock);
        var stampedElsewhere = CreateCup(spaceId, userId, new DateTime(2026, 6, 10, 9, 0, 0, DateTimeKind.Utc), clock);
        stampedElsewhere.AssignToBillingPeriod(BillingPeriodId.New());
        var beforeLedgerStart = CreateCup(spaceId, userId, new DateTime(2026, 5, 20, 9, 0, 0, DateTimeKind.Utc), clock);

        await using (var writeCtx = await _databaseFixture.CreateDbContextAsync())
        {
            await writeCtx.Consumptions.AddRangeAsync(stampedAfterEnd, unstampedInWindow, unstampedAfterEnd, stampedElsewhere, beforeLedgerStart);
            await writeCtx.SaveChangesAsync();
        }

        await using var readCtx = await _databaseFixture.CreateDbContextAsync();
        var cups = await readCtx.Consumptions
            .AsNoTracking()
            .Where(new LedgerConsumptionsSpecification(spaceId, periodEnd, ledgerStart, periodId).Criteria)
            .ToListAsync();

        cups.Select(c => c.Id).Should().BeEquivalentTo(new[] { stampedAfterEnd.Id, unstampedInWindow.Id });
    }

    [Fact]
    public async Task LedgerSpec_WithoutAPeriod_PrefiltersByLedgerStartOnly()
    {
        var clock = new TestClock();
        var spaceId = new SpaceId(Guid.NewGuid());
        var userId = new UserId(Guid.NewGuid());
        var ledgerStart = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        var older = CreateCup(spaceId, userId, new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc), clock);
        var newer = CreateCup(spaceId, userId, new DateTime(2026, 7, 5, 9, 0, 0, DateTimeKind.Utc), clock);
        newer.AssignToBillingPeriod(BillingPeriodId.New());

        await using (var writeCtx = await _databaseFixture.CreateDbContextAsync())
        {
            await writeCtx.Consumptions.AddRangeAsync(older, newer);
            await writeCtx.SaveChangesAsync();
        }

        await using var readCtx = await _databaseFixture.CreateDbContextAsync();
        var withStart = await readCtx.Consumptions.AsNoTracking()
            .Where(new LedgerConsumptionsSpecification(spaceId, null, ledgerStart).Criteria)
            .ToListAsync();
        var withoutStart = await readCtx.Consumptions.AsNoTracking()
            .Where(new LedgerConsumptionsSpecification(spaceId, null, null).Criteria)
            .ToListAsync();

        withStart.Select(c => c.Id).Should().BeEquivalentTo(new[] { newer.Id });
        withoutStart.Select(c => c.Id).Should().BeEquivalentTo(new[] { older.Id, newer.Id });
    }

    [Fact]
    public async Task LedgerSpec_WithoutAPeriod_SkipsCupsStampedBeforeTheLastSettledEnd()
    {
        var clock = new TestClock();
        var spaceId = new SpaceId(Guid.NewGuid());
        var userId = new UserId(Guid.NewGuid());
        var settledThrough = new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc);

        var settled = CreateCup(spaceId, userId, new DateTime(2026, 6, 10, 9, 0, 0, DateTimeKind.Utc), clock);
        settled.AssignToBillingPeriod(BillingPeriodId.New());
        var unstampedEarly = CreateCup(spaceId, userId, new DateTime(2026, 6, 12, 9, 0, 0, DateTimeKind.Utc), clock);
        var stampedLater = CreateCup(spaceId, userId, new DateTime(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc), clock);
        stampedLater.AssignToBillingPeriod(BillingPeriodId.New());

        await using (var writeCtx = await _databaseFixture.CreateDbContextAsync())
        {
            await writeCtx.Consumptions.AddRangeAsync(settled, unstampedEarly, stampedLater);
            await writeCtx.SaveChangesAsync();
        }

        await using var readCtx = await _databaseFixture.CreateDbContextAsync();
        var cups = await readCtx.Consumptions.AsNoTracking()
            .Where(new LedgerConsumptionsSpecification(spaceId, null, null, settledThrough: settledThrough).Criteria)
            .ToListAsync();

        cups.Select(c => c.Id).Should().BeEquivalentTo(new[] { unstampedEarly.Id, stampedLater.Id });
    }

    // T-INF-02: the B1 regression. Close stamped cups loaded AsNoTracking, so nothing was ever saved.
    [Fact]
    public async Task GetBySpecForUpdate_ThenStamp_PersistsTheStamp()
    {
        var clock = new TestClock();
        var spaceId = new SpaceId(Guid.NewGuid());
        var periodId = BillingPeriodId.New();
        var cup = CreateCup(spaceId, new UserId(Guid.NewGuid()), new DateTime(2026, 7, 5, 9, 0, 0, DateTimeKind.Utc), clock);

        await using (var writeCtx = await _databaseFixture.CreateDbContextAsync())
        {
            await writeCtx.Consumptions.AddAsync(cup);
            await writeCtx.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddPersistence(_databaseFixture.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IConsumptionRepository>();
            var cups = await repository.GetBySpecForUpdateAsync(
                new UnassignedConsumptionsInPeriodSpecification(spaceId, new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc)));
            cups.Should().ContainSingle().Which.AssignToBillingPeriod(periodId);
            await scope.ServiceProvider.GetRequiredService<BeanShareDbContext>().SaveChangesAsync();
        }

        await using var readCtx = await _databaseFixture.CreateDbContextAsync();
        var stored = await readCtx.Consumptions.AsNoTracking().SingleAsync(c => c.Id == cup.Id);
        stored.BillingPeriodId.Should().Be(periodId);
        stored.Version.Should().Be(1);
    }

    private static ConsumptionEntry CreateCup(SpaceId spaceId, UserId userId, DateTime consumedAt, IClock clock) =>
        ConsumptionEntry.Create(
            spaceId,
            userId,
            CoffeeProduct.Create("Test Coffee", "Test Brand", CoffeeType.Espresso),
            Weight.FromGrams(18m),
            consumedAt,
            clock);

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);
    }
}
