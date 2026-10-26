using BeanShare.Application.Common;
using BeanShare.Application.Features.Billing.Commands.CloseBillingPeriod;
using BeanShare.Application.Features.Billing.Commands.ReopenBillingPeriod;
using BeanShare.Application.Features.Settlement.Commands.GenerateSettlement;
using BeanShare.Domain.Enums;
using BeanShare.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BeanShare.Infrastructure.Tests.InMemory;

// T-INF-06 (C1 part): a stale or racing period transition can no longer regress a period.
[Collection(InMemoryContainerCollection.Name)]
public sealed class BillingPeriodConcurrencyTests
{
    [Fact(Timeout = 10000)]
    public async Task RacingClose_TheHeldCommandGetsConcurrencyConflict()
    {
        await using var container = await InMemoryContainer.CreateAsync();
        var scenario = await LedgerScenario.CreateAsync(container);
        var periodId = await scenario.CreateOpenPeriodAsync(LedgerScenario.Today.AddDays(-10), LedgerScenario.Today.AddDays(-1));

        container.Barrier.Arm();
        var held = Task.Run(() => container.SendAsync(scenario.Admin, new CloseBillingPeriodCommand(periodId)));
        await container.Barrier.WaitUntilHeldAsync();

        var winner = await container.SendAsync(scenario.Admin, new CloseBillingPeriodCommand(periodId));
        winner.IsSuccess.Should().BeTrue();

        container.Barrier.Release();
        var loser = await held;

        loser.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
        (await container.SendAsync(scenario.Admin, new ReopenBillingPeriodCommand(periodId))).IsSuccess.Should().BeTrue();
    }

    // T-INF-07: ports of Probe_ReopenRacingGenerate_* (InMemory asserts the outcome only, DS-39).
    [Fact(Timeout = 10000)]
    public async Task ReopenHeld_RacingGeneration_TheHeldReopenGetsConcurrencyConflict()
    {
        await using var container = await InMemoryContainer.CreateAsync();
        var scenario = await LedgerScenario.CreateAsync(container);
        var periodId = await scenario.CreateOpenPeriodAsync(LedgerScenario.Today.AddDays(-10), LedgerScenario.Today.AddDays(-1));
        await scenario.LogCupAsync(LedgerScenario.Today.AddDays(-5));
        (await container.SendAsync(scenario.Admin, new CloseBillingPeriodCommand(periodId))).IsSuccess.Should().BeTrue();

        container.Barrier.Arm();
        var held = Task.Run(() => container.SendAsync(scenario.Admin, new ReopenBillingPeriodCommand(periodId)));
        await container.Barrier.WaitUntilHeldAsync();

        (await container.SendAsync(scenario.Admin, new GenerateSettlementCommand(periodId))).IsSuccess.Should().BeTrue();

        container.Barrier.Release();
        (await held).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
    }

    [Fact(Timeout = 10000)]
    public async Task GenerationHeld_RacingReopen_TheHeldGenerationGetsConcurrencyConflict()
    {
        await using var container = await InMemoryContainer.CreateAsync();
        var scenario = await LedgerScenario.CreateAsync(container);
        var periodId = await scenario.CreateOpenPeriodAsync(LedgerScenario.Today.AddDays(-10), LedgerScenario.Today.AddDays(-1));
        await scenario.LogCupAsync(LedgerScenario.Today.AddDays(-5));
        (await container.SendAsync(scenario.Admin, new CloseBillingPeriodCommand(periodId))).IsSuccess.Should().BeTrue();

        container.Barrier.Arm();
        var held = Task.Run(() => container.SendAsync(scenario.Admin, new GenerateSettlementCommand(periodId)));
        await container.Barrier.WaitUntilHeldAsync();

        (await container.SendAsync(scenario.Admin, new ReopenBillingPeriodCommand(periodId))).IsSuccess.Should().BeTrue();

        container.Barrier.Release();
        (await held).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
        (await container.SendAsync(scenario.Admin, new CloseBillingPeriodCommand(periodId))).IsSuccess.Should().BeTrue();
    }

    // Port of Evidence_StaleCircuitClose_RegressesSettledToClosed, inverted.
    [Fact(Timeout = 10000)]
    public async Task StaleCircuitClose_AfterSettling_IsRefused()
    {
        await using var container = await InMemoryContainer.CreateAsync();
        var scenario = await LedgerScenario.CreateAsync(container);
        var periodId = await scenario.CreateOpenPeriodAsync(LedgerScenario.Today.AddDays(-10), LedgerScenario.Today.AddDays(-1));
        await scenario.LogCupAsync(LedgerScenario.Today.AddDays(-5));

        await using var circuit = container.Services.CreateAsyncScope();
        var circuitSender = circuit.ServiceProvider.GetRequiredService<IScopedSender>();

        (await container.SendAsync(scenario.Admin, new CloseBillingPeriodCommand(periodId))).IsSuccess.Should().BeTrue();
        (await container.SendAsync(scenario.Admin, new GenerateSettlementCommand(periodId))).IsSuccess.Should().BeTrue();

        container.User.Set(scenario.Admin);
        var staleClose = await circuitSender.Send(new CloseBillingPeriodCommand(periodId));

        staleClose.Errors.Should().ContainSingle().Which.Code
            .Should().BeOneOf(Error.Codes.InvalidBillingPeriodState, Error.Codes.ConcurrencyConflict);
        var state = await container.ReadAsync(db => db.BillingPeriods.AsNoTracking()
            .Where(p => p.Id == new BillingPeriodId(periodId)).Select(p => p.State).SingleAsync());
        state.Should().Be(BillingState.Settled);
    }
}
