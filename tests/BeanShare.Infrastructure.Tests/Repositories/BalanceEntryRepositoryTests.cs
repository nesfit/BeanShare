using BeanShare.Domain.Aggregates.Ledger;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.ValueObjects;
using BeanShare.Infrastructure.Persistence.Repositories;
using BeanShare.Infrastructure.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BeanShare.Infrastructure.Tests.Repositories;

// T-INF-01: balance entries round-trip and their Version token guards concurrent edits.
public sealed class BalanceEntryRepositoryTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _databaseFixture;

    public BalanceEntryRepositoryTests(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
    }

    private static BalanceEntry NewEntry(SpaceId spaceId, UserId member, UserId treasurer) =>
        BalanceEntry.Create(spaceId, member, treasurer, -538.25m, Currency.CZK,
            new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc), BalanceEntryKind.Opening, "Excel cutoff",
            treasurer, new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc), new TestClock());

    [Fact]
    public async Task Entries_RoundTripAndFilterByVisibilityAndInclusion()
    {
        var spaceId = SpaceId.New();
        var member = UserId.New();
        var treasurer = UserId.New();
        var entry = NewEntry(spaceId, member, treasurer);
        var other = NewEntry(spaceId, UserId.New(), treasurer);
        var included = NewEntry(spaceId, member, treasurer);
        included.MarkIncluded(SettlementId.New());

        await using (var writeCtx = await _databaseFixture.CreateDbContextAsync())
        {
            var repo = new BalanceEntryRepository(writeCtx);
            await repo.AddAsync(entry);
            await repo.AddAsync(other);
            await repo.AddAsync(included);
            await writeCtx.SaveChangesAsync();
        }

        await using var readCtx = await _databaseFixture.CreateDbContextAsync();
        var readRepo = new BalanceEntryRepository(readCtx);

        var loaded = await readRepo.GetByIdAsync(entry.Id);
        loaded.Should().NotBeNull();
        loaded!.Amount.Should().Be(-538.25m);
        loaded.CounterpartyUserId.Should().Be(treasurer);
        loaded.Kind.Should().Be(BalanceEntryKind.Opening);
        loaded.EffectiveDate.Should().Be(new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc));
        readCtx.Entry(loaded).State.Should().Be(EntityState.Unchanged, "GetByIdAsync is tracked");

        (await readRepo.GetVisibleAsync(spaceId, member)).Should().HaveCount(2);
        (await readRepo.GetVisibleAsync(spaceId, treasurer)).Should().HaveCount(3, "the counterparty sees every entry against them");
        (await readRepo.GetVisibleAsync(spaceId, null)).Should().HaveCount(3);
        (await readRepo.GetUnincludedForUpdateAsync(spaceId, new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc)))
            .Select(e => e.Id).Should().BeEquivalentTo(new[] { entry.Id, other.Id });
        (await readRepo.GetUnincludedForUpdateAsync(spaceId, new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc)))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentEdits_TheStaleOneRaisesAConcurrencyError()
    {
        var entry = NewEntry(SpaceId.New(), UserId.New(), UserId.New());
        await using (var writeCtx = await _databaseFixture.CreateDbContextAsync())
        {
            await new BalanceEntryRepository(writeCtx).AddAsync(entry);
            await writeCtx.SaveChangesAsync();
        }

        await using var first = await _databaseFixture.CreateDbContextAsync();
        await using var second = await _databaseFixture.CreateDbContextAsync();
        var a = await new BalanceEntryRepository(first).GetByIdAsync(entry.Id);
        var b = await new BalanceEntryRepository(second).GetByIdAsync(entry.Id);

        a!.MarkIncluded(SettlementId.New());
        await first.SaveChangesAsync();

        new BalanceEntryRepository(second).Remove(b!);
        var act = () => second.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
    }
}
