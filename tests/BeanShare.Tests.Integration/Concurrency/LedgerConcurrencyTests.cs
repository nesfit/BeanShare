using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Commands.CreateBalanceEntry;
using BeanShare.Application.Features.Balances.Commands.DeleteBalanceEntry;
using BeanShare.Application.Features.Balances.Commands.UpdateBalanceEntry;
using BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;
using BeanShare.Application.Features.Billing.Commands.CloseBillingPeriod;
using BeanShare.Application.Features.Billing.Commands.CreateBillingPeriod;
using BeanShare.Application.Features.Billing.Commands.ReopenBillingPeriod;
using BeanShare.Application.Features.Consumption.Commands;
using BeanShare.Application.Features.Settlement.Commands.ConfirmPayment;
using BeanShare.Application.Features.Settlement.Commands.GenerateSettlement;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Enums;
using BeanShare.Domain.ValueObjects;
using BeanShare.Tests.Integration.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BeanShare.Tests.Integration.Concurrency;

// T-INT-03..07b and the Npgsql variants of T-INF-07 and T-MEM-01: every race in design §6 ends in
// CONCURRENCY_CONFLICT for the held command, with nothing lost or counted twice.
public sealed class LedgerConcurrencyTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private NpgsqlAppContainer _container = null!;

    public LedgerConcurrencyTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _container = NpgsqlAppContainer.Create(_fixture.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private static DateTime Today => LedgerScenario.Today;

    private async Task<(Result<TResponse> Held, TOther Winner)> RaceAsync<TResponse, TOther>(
        Func<Task<Result<TResponse>>> held, Func<Task<TOther>> winner)
    {
        _container.Barrier.Arm();
        var heldTask = Task.Run(held);
        await _container.Barrier.WaitUntilHeldAsync();
        var winnerResult = await winner();
        _container.Barrier.Release();
        return (await heldTask, winnerResult);
    }

    private async Task<(Result Held, TOther Winner)> RaceAsync<TOther>(Func<Task<Result>> held, Func<Task<TOther>> winner)
    {
        _container.Barrier.Arm();
        var heldTask = Task.Run(held);
        await _container.Barrier.WaitUntilHeldAsync();
        var winnerResult = await winner();
        _container.Barrier.Release();
        return (await heldTask, winnerResult);
    }

    // T-INT-03: entry edit held, generation wins.
    [Fact]
    public async Task EntryEditHeld_RacingGeneration_FailsAndTheSettlementKeepsTheEntry()
    {
        var s = await LedgerScenario.CreateAsync(_container);
        var entry = (await _container.SendAsync(s.Admin, new CreateBalanceEntryCommand(s.SpaceId, s.Alice.Value, -50m, Today.AddDays(-20), "Excel cutoff"))).Value;
        var period = await s.ClosedPeriodAsync(Today.AddDays(-20), Today.AddDays(-1));

        var (held, winner) = await RaceAsync(
            () => _container.SendAsync(s.Admin, new UpdateBalanceEntryCommand(s.SpaceId, entry.Id, s.Alice.Value, s.Admin.Value, -70m, Today.AddDays(-20), "edited", BalanceEntryKind.Opening)),
            () => _container.SendAsync(s.Admin, new GenerateSettlementCommand(period)));

        winner.IsSuccess.Should().BeTrue();
        held.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
        winner.Value.Lines.Single(l => l.UserId == s.Alice.Value).Adjustments.Should().Be(-50m);
        var stored = await _container.ReadAsync(db => db.BalanceEntries.AsNoTracking().SingleAsync(e => e.Id == new BalanceEntryId(entry.Id)));
        stored.Amount.Should().Be(-50m);
        stored.IncludedInSettlementId.Should().NotBeNull();
    }

    // T-INT-03, other order: generation held, the entry is deleted meanwhile.
    [Fact]
    public async Task GenerationHeld_RacingEntryDelete_FailsAtomically()
    {
        var s = await LedgerScenario.CreateAsync(_container);
        var entry = (await _container.SendAsync(s.Admin, new CreateBalanceEntryCommand(s.SpaceId, s.Alice.Value, -50m, Today.AddDays(-20), "Excel cutoff"))).Value;
        var period = await s.ClosedPeriodAsync(Today.AddDays(-20), Today.AddDays(-1));

        var (held, winner) = await RaceAsync(
            () => _container.SendAsync(s.Admin, new GenerateSettlementCommand(period)),
            () => _container.SendAsync(s.Admin, new DeleteBalanceEntryCommand(s.SpaceId, entry.Id)));

        winner.IsSuccess.Should().BeTrue();
        held.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
        (await _container.ReadAsync(db => db.Settlements.AnyAsync(x => x.BillingPeriodId == new BillingPeriodId(period))))
            .Should().BeFalse("the failed generation left nothing behind");

        var retry = await _container.SendAsync(s.Admin, new GenerateSettlementCommand(period));
        retry.IsSuccess.Should().BeTrue();
        retry.Value.Lines.Should().NotContain(l => l.UserId == s.Alice.Value);
    }

    // T-INT-04: a confirm on S(n) racing the generation of S(n+1) never counts the line twice.
    [Fact]
    public async Task ConfirmHeld_RacingTheNextGeneration_FailsAndTheLineIsCarriedOnce()
    {
        var s = await LedgerScenario.CreateAsync(_container);
        await _container.SendAsync(s.Admin, new CreateBalanceEntryCommand(s.SpaceId, s.Alice.Value, -50m, Today.AddDays(-30), "Excel cutoff"));
        var first = await s.ClosedPeriodAsync(Today.AddDays(-30), Today.AddDays(-20));
        var s1 = (await _container.SendAsync(s.Admin, new GenerateSettlementCommand(first))).Value;
        var second = await s.ClosedPeriodAsync(Today.AddDays(-19), Today.AddDays(-1));

        var (held, winner) = await RaceAsync(
            () => _container.SendAsync(s.Alice, new ConfirmPaymentCommand(s1.Id, s.Alice.Value)),
            () => _container.SendAsync(s.Admin, new GenerateSettlementCommand(second)));

        winner.IsSuccess.Should().BeTrue();
        held.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
        winner.Value.Lines.Single(l => l.UserId == s.Alice.Value).CarriedIn.Should().Be(-50m);
        var line = await _container.ReadAsync(db => db.Settlements.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.Id == new SettlementId(s1.Id)).SelectMany(x => x.Lines).SingleAsync(l => l.UserId == s.Alice));
        line.Resolution.Should().Be(LineResolution.Carried);
        line.IsConfirmed.Should().BeFalse();
    }

    // T-INT-05: the last two confirms race; the retry resolves the treasurer.
    [Fact]
    public async Task LastTwoConfirms_TheHeldOneFails_AndTheRetryCompletesTheSettlement()
    {
        var s = await LedgerScenario.CreateAsync(_container);
        await _container.SendAsync(s.Admin, new CreateBalanceEntryCommand(s.SpaceId, s.Alice.Value, -50m, Today.AddDays(-20), "Excel cutoff"));
        await _container.SendAsync(s.Admin, new CreateBalanceEntryCommand(s.SpaceId, s.Bob.Value, 20m, Today.AddDays(-20), "Excel cutoff"));
        var period = await s.ClosedPeriodAsync(Today.AddDays(-20), Today.AddDays(-1));
        var settlement = (await _container.SendAsync(s.Admin, new GenerateSettlementCommand(period))).Value;

        var (held, winner) = await RaceAsync(
            () => _container.SendAsync(s.Alice, new ConfirmPaymentCommand(settlement.Id, s.Alice.Value)),
            () => _container.SendAsync(s.Bob, new ConfirmPaymentCommand(settlement.Id, s.Bob.Value)));

        winner.IsSuccess.Should().BeTrue();
        held.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);

        (await _container.SendAsync(s.Alice, new ConfirmPaymentCommand(settlement.Id, s.Alice.Value))).IsSuccess.Should().BeTrue();
        var stored = await _container.ReadAsync(db => db.Settlements.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == new SettlementId(settlement.Id)));
        stored.Status.Should().Be(SettlementStatus.Completed);
        stored.Lines.Single(l => l.UserId == s.Admin).Resolution.Should().Be(LineResolution.Confirmed);
    }

    // T-INT-06: a cup edit racing the generation that stamps it. Closing a period stamps its cups (B1), and a cup
    // stamped to a Closed period cannot be edited at all, so the cup is logged, back-dated, after the close: the
    // generation is what stamps it.
    [Fact]
    public async Task CupEditHeld_RacingGeneration_Fails()
    {
        var s = await LedgerScenario.CreateAsync(_container);
        var period = await s.ClosedPeriodAsync(Today.AddDays(-20), Today.AddDays(-1));
        var consumedAt = Today.AddDays(-5).AddHours(9);
        var cupId = await s.LogCupAsync(s.Alice, consumedAt);
        (await _container.ReadAsync(db => db.Consumptions.AsNoTracking().SingleAsync(c => c.Id == new ConsumptionEntryId(cupId))))
            .BillingPeriodId.Should().BeNull("the generation, not the close, stamps this cup");

        var (held, winner) = await RaceAsync(
            () => _container.SendAsync(s.Alice, new UpdateConsumptionCommand(cupId, s.SpaceId, "Alfa", "Roaster", "Filter", 12m, consumedAt)),
            () => _container.SendAsync(s.Admin, new GenerateSettlementCommand(period)));

        winner.IsSuccess.Should().BeTrue();
        held.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
        winner.Value.Lines.Single(l => l.UserId == s.Alice.Value).TotalCoffeeGrams.Should().Be(10m);
        (await _container.ReadAsync(db => db.Consumptions.AsNoTracking().SingleAsync(c => c.Id == new ConsumptionEntryId(cupId))))
            .Quantity.Grams.Should().Be(10m, "the held edit saved nothing");
    }

    // T-INT-07: two generations of the same period, and generation against a settings change.
    [Fact]
    public async Task TwoGenerationsOfTheSamePeriod_TheHeldOneGets409NotA500()
    {
        var s = await LedgerScenario.CreateAsync(_container);
        var period = await s.ClosedPeriodAsync(Today.AddDays(-20), Today.AddDays(-1));

        var (held, winner) = await RaceAsync(
            () => _container.SendAsync(s.Admin, new GenerateSettlementCommand(period)),
            () => _container.SendAsync(s.Admin, new GenerateSettlementCommand(period)));

        winner.IsSuccess.Should().BeTrue();
        held.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
        (await _container.ReadAsync(db => db.Settlements.CountAsync(x => x.BillingPeriodId == new BillingPeriodId(period)))).Should().Be(1);
    }

    // Two creates of the same period (a double submit, two admins): the held one loses, and no twin period is stored.
    [Fact]
    public async Task TwoCreatesOfTheSamePeriod_TheHeldOneGets409_AndOnlyOneIsStored()
    {
        var s = await LedgerScenario.CreateAsync(_container);
        var create = new CreateBillingPeriodCommand(s.SpaceId, "Next", Today.AddDays(1), Today.AddDays(20));

        var (held, winner) = await RaceAsync(
            () => _container.SendAsync(s.Admin, create),
            () => _container.SendAsync(s.Admin, create));

        winner.IsSuccess.Should().BeTrue();
        held.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
        (await _container.ReadAsync(db => db.BillingPeriods.CountAsync(p => p.SpaceId == new SpaceId(s.SpaceId) && p.Name == "Next"))).Should().Be(1);
    }

    [Fact]
    public async Task GenerationHeld_RacingALedgerSettingsChange_Gets409()
    {
        var s = await LedgerScenario.CreateAsync(_container);
        var period = await s.ClosedPeriodAsync(Today.AddDays(-20), Today.AddDays(-1));

        var (held, winner) = await RaceAsync(
            () => _container.SendAsync(s.Admin, new GenerateSettlementCommand(period)),
            () => _container.SendAsync(s.Admin, new UpdateLedgerSettingsCommand(s.SpaceId, s.Admin.Value, Today.AddDays(-25))));

        winner.IsSuccess.Should().BeTrue();
        held.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
    }

    // T-INT-07b / T-INF-06 (Npgsql): a stale close after settling never regresses the period.
    [Fact]
    public async Task CloseHeld_RacingCloseAndSettle_FailsAndThePeriodStaysSettled()
    {
        var s = await LedgerScenario.CreateAsync(_container);
        var period = (await _container.SendAsync(s.Admin, new Application.Features.Billing.Commands.CreateBillingPeriod.CreateBillingPeriodCommand(
            s.SpaceId, "Race", Today.AddDays(-20), Today.AddDays(-1)))).Value.Id;
        await _container.SendAsync(s.Admin, new Application.Features.Billing.Commands.OpenBillingPeriod.OpenBillingPeriodCommand(period));

        var (held, winner) = await RaceAsync(
            () => _container.SendAsync(s.Admin, new CloseBillingPeriodCommand(period)),
            async () =>
            {
                (await _container.SendAsync(s.Admin, new CloseBillingPeriodCommand(period))).IsSuccess.Should().BeTrue();
                return await _container.SendAsync(s.Admin, new GenerateSettlementCommand(period));
            });

        winner.IsSuccess.Should().BeTrue();
        held.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
        (await PeriodAsync(period)).State.Should().Be(BillingState.Settled);
    }

    // T-INT-07b / T-INF-07 (Npgsql): exactly one of reopen and generation commits; no settled period ends in 9999.
    [Fact]
    public async Task ReopenHeld_RacingGeneration_ExactlyOneCommits()
    {
        var s = await LedgerScenario.CreateAsync(_container);
        var period = await s.ClosedPeriodAsync(Today.AddDays(-20), Today.AddDays(-1));

        var (held, winner) = await RaceAsync(
            () => _container.SendAsync(s.Admin, new ReopenBillingPeriodCommand(period)),
            () => _container.SendAsync(s.Admin, new GenerateSettlementCommand(period)));

        winner.IsSuccess.Should().BeTrue();
        held.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
        var stored = await PeriodAsync(period);
        stored.State.Should().Be(BillingState.Settled);
        stored.IsOpenEnded.Should().BeFalse();
    }

    [Fact]
    public async Task GenerationHeld_RacingReopen_ExactlyOneCommits()
    {
        var s = await LedgerScenario.CreateAsync(_container);
        var period = await s.ClosedPeriodAsync(Today.AddDays(-20), Today.AddDays(-1));

        var (held, winner) = await RaceAsync(
            () => _container.SendAsync(s.Admin, new GenerateSettlementCommand(period)),
            () => _container.SendAsync(s.Admin, new ReopenBillingPeriodCommand(period)));

        winner.IsSuccess.Should().BeTrue();
        held.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
        (await PeriodAsync(period)).State.Should().Be(BillingState.Open);
        (await _container.ReadAsync(db => db.Settlements.AnyAsync(x => x.BillingPeriodId == new BillingPeriodId(period)))).Should().BeFalse();
    }

    // T-MEM-01 (Npgsql): an entry written while the member is removed conflicts, so no entry outlives the membership.
    [Fact]
    public async Task EntryCreateHeld_RacingRemoveMember_Gets409()
    {
        var s = await LedgerScenario.CreateAsync(_container);

        var (held, winner) = await RaceAsync(
            () => _container.SendAsync(s.Admin, new CreateBalanceEntryCommand(s.SpaceId, s.Bob.Value, -5m, Today, "late")),
            () => _container.SendAsync(s.Admin, new RemoveMemberCommand(s.SpaceId, s.Bob.Value)));

        winner.IsSuccess.Should().BeTrue();
        held.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
        (await _container.ReadAsync(db => db.BalanceEntries.AnyAsync(e => e.UserId == s.Bob))).Should().BeFalse();
    }

    private Task<BillingPeriod> PeriodAsync(Guid id) =>
        _container.ReadAsync(db => db.BillingPeriods.AsNoTracking().SingleAsync(p => p.Id == new BillingPeriodId(id)));
}
