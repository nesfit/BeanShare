using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using BeanShare.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
    /// <see cref="UnsettledConsumptionsSpecification"/> mixes a nullable-DateTime closure with a
    /// null check on a value-converted <c>BillingPeriodId?</c>. Both have to survive translation to
    /// SQL, so this exercises it against a real Npgsql provider rather than LINQ-to-objects.
    /// </summary>
    [Fact]
    public async Task GetBySpecAsync_WithUnsettledSpec_ShouldTranslateAndSkipBilledCups()
    {
        var clock = new TestClock();
        var spaceId = new SpaceId(Guid.NewGuid());
        var userId = new UserId(Guid.NewGuid());
        var settledPeriodId = BillingPeriodId.New();
        var watermark = new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc);

        var billed = CreateCup(spaceId, userId, new DateTime(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc), clock);
        billed.AssignToBillingPeriod(settledPeriodId);

        // Back-dated into the settled window but never assigned — no settlement covered it.
        var strayBeforeWatermark = CreateCup(spaceId, userId, new DateTime(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc), clock);
        var afterWatermark = CreateCup(spaceId, userId, new DateTime(2026, 7, 5, 9, 0, 0, DateTimeKind.Utc), clock);

        await using (var writeCtx = await _databaseFixture.CreateDbContextAsync())
        {
            await writeCtx.Consumptions.AddRangeAsync(billed, strayBeforeWatermark, afterWatermark);
            await writeCtx.SaveChangesAsync();
        }

        // Mirrors ConsumptionRepository.GetBySpecAsync, which is internal to the persistence layer.
        await using var readCtx = await _databaseFixture.CreateDbContextAsync();
        var candidates = await readCtx.Consumptions
            .AsNoTracking()
            .Where(new UnsettledConsumptionsSpecification(spaceId, watermark).Criteria)
            .ToListAsync();

        candidates.Select(c => c.Id).Should().BeEquivalentTo(
            new[] { strayBeforeWatermark.Id, afterWatermark.Id });
    }

    [Fact]
    public async Task GetBySpecAsync_WithUnsettledSpecAndNoWatermark_ShouldReturnEveryCup()
    {
        var clock = new TestClock();
        var spaceId = new SpaceId(Guid.NewGuid());
        var userId = new UserId(Guid.NewGuid());

        var older = CreateCup(spaceId, userId, new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc), clock);
        var newer = CreateCup(spaceId, userId, new DateTime(2026, 7, 5, 9, 0, 0, DateTimeKind.Utc), clock);
        newer.AssignToBillingPeriod(BillingPeriodId.New());

        await using (var writeCtx = await _databaseFixture.CreateDbContextAsync())
        {
            await writeCtx.Consumptions.AddRangeAsync(older, newer);
            await writeCtx.SaveChangesAsync();
        }

        await using var readCtx = await _databaseFixture.CreateDbContextAsync();
        var candidates = await readCtx.Consumptions
            .AsNoTracking()
            .Where(new UnsettledConsumptionsSpecification(spaceId).Criteria)
            .ToListAsync();

        candidates.Select(c => c.Id).Should().BeEquivalentTo(new[] { older.Id, newer.Id });
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
