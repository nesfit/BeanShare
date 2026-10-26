using BeanShare.Domain.Aggregates.Settlement;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.ValueObjects;
using BeanShare.Infrastructure.Persistence.Repositories;
using BeanShare.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace BeanShare.Infrastructure.Tests.Repositories;

public sealed class SettlementRepositoryTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _databaseFixture;

    public SettlementRepositoryTests(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
    }

    /// <summary>
    /// Regression test: verifies that <see cref="SettlementRepository.UpdateAsync"/> correctly
    /// persists a payment confirmation change made via <c>ConfirmPayment</c>.
    /// Previously the repository called <c>context.Settlements.Update(settlement)</c> on an
    /// already-tracked aggregate. This test ensures the fix (removing that call) does not
    /// regress payment confirmation persistence.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_WhenPaymentConfirmed_ShouldPersistConfirmation()
    {
        var clock = new TestClock();
        var spaceId = new SpaceId(Guid.NewGuid());
        var billingPeriodId = BillingPeriodId.New();
        var generatorId = new UserId(Guid.NewGuid());
        var memberId = new UserId(Guid.NewGuid());
        SettlementId settlementId;

        // Arrange: create and persist a settlement with one line requiring payment
        await using (var writeCtx = await _databaseFixture.CreateDbContextAsync())
        {
            var settlement = Settlement.Create(spaceId, billingPeriodId, "USD", generatorId, clock);
            settlementId = settlement.Id;

            settlement.AddLine(memberId, totalCoffeeGrams: 100, amountDue: 5.00m, clock);
            settlement.FinalizeGeneration(clock);

            await writeCtx.Settlements.AddAsync(settlement);
            await writeCtx.SaveChangesAsync();
        }

        // Act: load in a fresh context, confirm payment, call UpdateAsync, save
        await using (var updateCtx = await _databaseFixture.CreateDbContextAsync())
        {
            var repo = new SettlementRepository(updateCtx);
            var settlement = await repo.GetByIdAsync(settlementId);
            settlement.Should().NotBeNull();

            settlement!.ConfirmPayment(memberId, generatorId, clock);
            await repo.UpdateAsync(settlement);
            await updateCtx.SaveChangesAsync();
        }

        // Assert: reload in a third context and verify the confirmation is persisted
        await using var readCtx = await _databaseFixture.CreateDbContextAsync();
        var readRepo = new SettlementRepository(readCtx);
        var reloaded = await readRepo.GetByIdAsync(settlementId);

        reloaded.Should().NotBeNull();
        var line = reloaded!.GetLineForUser(memberId);
        line.Should().NotBeNull();
        line!.IsConfirmed.Should().BeTrue("the payment should have been persisted by UpdateAsync + SaveChanges");
    }

    // T-INF-04: ledger line snapshots round-trip.
    [Fact]
    public async Task LedgerSettlement_RoundTripsSnapshotsAndCarryColumns()
    {
        var clock = new TestClock();
        var spaceId = new SpaceId(Guid.NewGuid());
        var treasurer = new UserId(Guid.NewGuid());
        var member = new UserId(Guid.NewGuid());
        var previous = SettlementId.New();

        var settlement = Settlement.CreateLedger(spaceId, BillingPeriodId.New(), "CZK", treasurer, treasurer, 1, clock);
        settlement.AddLedgerLine(new LedgerLineSnapshot(member, 100m, null, 78m, 0m, -10m, -538m, 0m), previous, clock);
        settlement.AddLedgerLine(new LedgerLineSnapshot(treasurer, 60m, null, 42m, 75m, 10m, 538m, 0.01m), previous, clock);
        settlement.FinalizeGeneration(clock);

        await using (var writeCtx = await _databaseFixture.CreateDbContextAsync())
        {
            await new SettlementRepository(writeCtx).AddAsync(settlement);
            await writeCtx.SaveChangesAsync();
        }

        await using var readCtx = await _databaseFixture.CreateDbContextAsync();
        var repo = new SettlementRepository(readCtx);
        var reloaded = await repo.GetLatestLedgerForUpdateAsync(spaceId);

        reloaded.Should().NotBeNull();
        reloaded!.Model.Should().Be(SettlementModel.Ledger);
        reloaded.Sequence.Should().Be(1);
        reloaded.TreasurerUserId.Should().Be(treasurer);
        var line = reloaded.GetLineForUser(member)!;
        line.AmountDue.Amount.Should().Be(78m);
        line.CreditAmount.Should().Be(0m);
        line.CarriedInAmount.Should().Be(-10m);
        line.AdjustmentsAmount.Should().Be(-538m);
        line.NetAmount.Should().Be(-626m);
        line.Resolution.Should().Be(LineResolution.Open);
        line.CarriedFromSettlementId.Should().Be(previous);
        reloaded.GetLineForUser(treasurer)!.RoundingAmount.Should().Be(0.01m);
        (await repo.GetMaxSequenceAsync(spaceId)).Should().Be(1);
        (await repo.HasLedgerSettlementAsync(spaceId)).Should().BeTrue();
    }

    // T-INF-04: the filtered unique indexes reject a second settlement with the same sequence.
    [Fact]
    public async Task DuplicateSequence_InTheSameSpace_IsRejected()
    {
        var clock = new TestClock();
        var spaceId = new SpaceId(Guid.NewGuid());
        var treasurer = new UserId(Guid.NewGuid());

        await using var ctx = await _databaseFixture.CreateDbContextAsync();
        await ctx.Settlements.AddAsync(Settlement.CreateLedger(spaceId, BillingPeriodId.New(), "CZK", treasurer, treasurer, 1, clock));
        await ctx.SaveChangesAsync();

        await ctx.Settlements.AddAsync(Settlement.CreateLedger(spaceId, BillingPeriodId.New(), "CZK", treasurer, treasurer, 1, clock));
        var act = () => ctx.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>())
            .Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.ConstraintName.Should().Be("IX_Settlements_SpaceId_Sequence");
    }

    // T-INF-04: an open line is carried at most once (I6).
    [Fact]
    public async Task DuplicateCarry_FromTheSameSettlementForTheSameUser_IsRejected()
    {
        var clock = new TestClock();
        var spaceId = new SpaceId(Guid.NewGuid());
        var treasurer = new UserId(Guid.NewGuid());
        var carriedFrom = SettlementId.New();

        await using var ctx = await _databaseFixture.CreateDbContextAsync();
        var first = Settlement.CreateLedger(spaceId, BillingPeriodId.New(), "CZK", treasurer, treasurer, 2, clock);
        first.AddLedgerLine(new LedgerLineSnapshot(treasurer, 0m, null, 0m, 0m, 5m, 0m, 0m), carriedFrom, clock);
        await ctx.Settlements.AddAsync(first);
        await ctx.SaveChangesAsync();

        var second = Settlement.CreateLedger(spaceId, BillingPeriodId.New(), "CZK", treasurer, treasurer, 3, clock);
        second.AddLedgerLine(new LedgerLineSnapshot(treasurer, 0m, null, 0m, 0m, 5m, 0m, 0m), carriedFrom, clock);
        await ctx.Settlements.AddAsync(second);
        var act = () => ctx.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>())
            .Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.ConstraintName.Should().Be("IX_SettlementLines_CarriedFromSettlementId_UserId");
    }

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => new(2025, 9, 14, 12, 0, 0, DateTimeKind.Utc);
    }
}
