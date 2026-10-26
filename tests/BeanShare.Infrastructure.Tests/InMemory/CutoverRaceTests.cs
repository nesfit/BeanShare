using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;
using BeanShare.Application.Features.Billing.Commands.CloseBillingPeriod;
using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Application.Features.CoffeeStock.Commands.SetBagPaidInExcel;
using BeanShare.Application.Features.CoffeeStock.Queries;
using BeanShare.Application.Features.Consumption.Commands.VoidPhantomRows;
using BeanShare.Application.Features.Settlement.Commands.GenerateSettlement;

namespace BeanShare.Infrastructure.Tests.InMemory;

// T-CUT-01 and T-CUT-05 race variants (AD7 R7-2, AD9 N-1 inverted): every cutover input shares the space token.
[Collection(InMemoryContainerCollection.Name)]
public sealed class CutoverRaceTests
{
    private static DateTime LedgerStart => LedgerScenario.Today.AddDays(-15);

    private static async Task<(InMemoryContainer Container, LedgerScenario Scenario, Guid PeriodId, Guid BagId)> ArrangeAsync()
    {
        var container = await InMemoryContainer.CreateAsync();
        var scenario = await LedgerScenario.CreateAsync(container);
        (await container.SendAsync(scenario.Admin, new UpdateLedgerSettingsCommand(scenario.SpaceId, null, LedgerStart))).IsSuccess.Should().BeTrue();
        var periodId = await scenario.CreateOpenPeriodAsync(LedgerScenario.Today.AddDays(-10), LedgerScenario.Today.AddDays(-1));
        await scenario.LogCupAsync(LedgerScenario.Today.AddDays(-5));
        (await container.SendAsync(scenario.Admin, new CloseBillingPeriodCommand(periodId))).IsSuccess.Should().BeTrue();
        var stock = await container.SendAsync(scenario.Admin, new GetSpaceStockQuery(scenario.SpaceId));
        return (container, scenario, periodId, stock.Value.RecentPurchases.Single().Id);
    }

    [Fact(Timeout = 10000)]
    public async Task AHeldFlag_RacingAGeneration_GetsConcurrencyConflict()
    {
        var (container, scenario, periodId, bagId) = await ArrangeAsync();
        await using var _ = container;

        container.Barrier.Arm();
        var held = Task.Run(() => container.SendAsync(scenario.Admin, new SetBagPaidInExcelCommand(scenario.SpaceId, bagId, true)));
        await container.Barrier.WaitUntilHeldAsync();

        (await container.SendAsync(scenario.Admin, new GenerateSettlementCommand(periodId))).IsSuccess.Should().BeTrue();

        container.Barrier.Release();
        (await held).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
    }

    [Fact(Timeout = 10000)]
    public async Task AHeldLedgerStartChange_RacingABagDateChange_GetsConcurrencyConflict()
    {
        var (container, scenario, _, bagId) = await ArrangeAsync();
        await using var _ = container;

        container.Barrier.Arm();
        var held = Task.Run(() => container.SendAsync(scenario.Admin,
            new UpdateLedgerSettingsCommand(scenario.SpaceId, null, LedgerStart.AddDays(-1))));
        await container.Barrier.WaitUntilHeldAsync();

        (await container.SendAsync(scenario.Admin, new UpdateStockPurchaseCommand(
            scenario.SpaceId, bagId, 1000m, 20m, LedgerScenario.Today.AddDays(-19)))).IsSuccess.Should().BeTrue();

        container.Barrier.Release();
        (await held).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
    }

    [Fact(Timeout = 10000)]
    public async Task AHeldVoid_RacingAGeneration_GetsConcurrencyConflict()
    {
        var (container, scenario, periodId, bagId) = await ArrangeAsync();
        await using var _ = container;
        (await container.SendAsync(scenario.Admin, new MarkBagEmptyCommand(scenario.SpaceId, bagId))).IsSuccess.Should().BeTrue();
        var shares = await container.ReadAsync(db => Task.FromResult(db.Consumptions.Where(c => c.IsWriteOff).Select(c => c.Id.Value).ToList()));
        shares.Should().NotBeEmpty();

        container.Barrier.Arm();
        var held = Task.Run(() => container.SendAsync(scenario.Admin, new VoidPhantomRowsCommand(scenario.SpaceId, shares, "phantom")));
        await container.Barrier.WaitUntilHeldAsync();

        (await container.SendAsync(scenario.Admin, new GenerateSettlementCommand(periodId))).IsSuccess.Should().BeTrue();

        container.Barrier.Release();
        (await held).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
    }
}
