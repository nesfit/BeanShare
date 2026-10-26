using BeanShare.Application.Common;
using BeanShare.Application.Features.Billing.Commands.CloseBillingPeriod;
using BeanShare.Application.Features.Consumption.Commands;
using BeanShare.Application.Features.Settlement.Commands.GenerateSettlement;
using BeanShare.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BeanShare.Infrastructure.Tests.InMemory;

// T-INT-06 on InMemory (fix round L, L1): a cup edit races the generation that stamps the cup. Closing a period
// stamps its cups (B1), and a cup stamped to a Closed period cannot be edited, so the race needs a cup that the
// close did not stamp: one logged, back-dated, after the close.
[Collection(InMemoryContainerCollection.Name)]
public sealed class CupEditRaceTests
{
    private static DateTime Today => LedgerScenario.Today;

    [Fact(Timeout = 10000)]
    public async Task ACupStampedByTheClose_IsRefusedBeforeAnySave()
    {
        await using var container = await InMemoryContainer.CreateAsync();
        var scenario = await LedgerScenario.CreateAsync(container);
        var consumedAt = Today.AddDays(-5).AddHours(9);
        var cupId = await scenario.LogCupAsync(consumedAt);
        var periodId = await scenario.CreateOpenPeriodAsync(Today.AddDays(-20), Today.AddDays(-1));
        (await container.SendAsync(scenario.Admin, new CloseBillingPeriodCommand(periodId))).IsSuccess.Should().BeTrue();
        (await container.ReadAsync(db => db.Consumptions.AsNoTracking().SingleAsync(c => c.Id == new ConsumptionEntryId(cupId))))
            .BillingPeriodId.Should().Be(new BillingPeriodId(periodId), "the close stamps the period's cups (B1)");

        container.Barrier.Arm();
        var edit = await container.SendAsync(scenario.Admin,
            new UpdateConsumptionCommand(cupId, scenario.SpaceId, LedgerScenario.ProductName, LedgerScenario.ProductBrand, "Filter", 12m, consumedAt));

        edit.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.InvalidBillingPeriodState);
        container.Barrier.WaitUntilHeldAsync().IsCompleted.Should().BeFalse("the edit is refused before it saves, so a barrier never holds it");
    }

    [Fact(Timeout = 10000)]
    public async Task ACupEditHeld_RacingTheGenerationThatStampsIt_GetsConcurrencyConflict()
    {
        await using var container = await InMemoryContainer.CreateAsync();
        var scenario = await LedgerScenario.CreateAsync(container);
        var periodId = await scenario.CreateOpenPeriodAsync(Today.AddDays(-20), Today.AddDays(-1));
        (await container.SendAsync(scenario.Admin, new CloseBillingPeriodCommand(periodId))).IsSuccess.Should().BeTrue();
        var consumedAt = Today.AddDays(-5).AddHours(9);
        var cupId = await scenario.LogCupAsync(consumedAt);
        (await container.ReadAsync(db => db.Consumptions.AsNoTracking().SingleAsync(c => c.Id == new ConsumptionEntryId(cupId))))
            .BillingPeriodId.Should().BeNull("logged after the close, the cup waits for the generation to stamp it");

        container.Barrier.Arm();
        var held = Task.Run(() => container.SendAsync(scenario.Admin,
            new UpdateConsumptionCommand(cupId, scenario.SpaceId, LedgerScenario.ProductName, LedgerScenario.ProductBrand, "Filter", 12m, consumedAt)));
        await container.Barrier.WaitUntilHeldAsync();

        var generation = await container.SendAsync(scenario.Admin, new GenerateSettlementCommand(periodId));
        generation.IsSuccess.Should().BeTrue();

        container.Barrier.Release();
        // InMemory asserts the outcome only (DS-39); the Npgsql variant checks the stored grams.
        (await held).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
    }
}
