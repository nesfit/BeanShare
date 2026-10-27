using BeanShare.Application.Abstractions;
using BeanShare.Application.Features.Billing.Commands.CloseBillingPeriod;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using BeanShare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BeanShare.Infrastructure.Tests.InMemory;

// A regression test without Docker: closing a period really saves the cup stamps.
[Collection(InMemoryContainerCollection.Name)]
public sealed class CloseStampingTests
{
    [Fact(Timeout = 10000)]
    public async Task Close_PersistsTheStampsOfItsCups()
    {
        await using var container = await InMemoryContainer.CreateAsync();
        var scenario = await LedgerScenario.CreateAsync(container);
        var periodId = await scenario.CreateOpenPeriodAsync(LedgerScenario.Today.AddDays(-10), LedgerScenario.Today.AddDays(-1));
        var cupId = await scenario.LogCupAsync(LedgerScenario.Today.AddDays(-5));

        (await container.SendAsync(scenario.Admin, new CloseBillingPeriodCommand(periodId))).IsSuccess.Should().BeTrue();

        var stamp = await container.ReadAsync(db => db.Consumptions.AsNoTracking()
            .Where(c => c.Id == new ConsumptionEntryId(cupId)).Select(c => c.BillingPeriodId).SingleAsync());
        stamp.Should().Be(new BillingPeriodId(periodId));
    }

    [Fact(Timeout = 10000)]
    public async Task StampingCupsReadAsNoTracking_PersistsNothing()
    {
        await using var container = await InMemoryContainer.CreateAsync();
        var scenario = await LedgerScenario.CreateAsync(container);
        var cupId = await scenario.LogCupAsync(LedgerScenario.Today.AddDays(-5));
        var periodId = BillingPeriodId.New();

        await using (var scope = container.Services.CreateAsyncScope())
        {
            var cups = await scope.ServiceProvider.GetRequiredService<IConsumptionRepository>().GetBySpecAsync(
                new UnassignedConsumptionsInPeriodSpecification(new SpaceId(scenario.SpaceId), LedgerScenario.Today.AddDays(-10), LedgerScenario.Today));
            foreach (var cup in cups)
                cup.AssignToBillingPeriod(periodId);
            await scope.ServiceProvider.GetRequiredService<BeanShareDbContext>().SaveChangesAsync();
        }

        var stamp = await container.ReadAsync(db => db.Consumptions.AsNoTracking()
            .Where(c => c.Id == new ConsumptionEntryId(cupId)).Select(c => c.BillingPeriodId).SingleAsync());
        stamp.Should().BeNull("the old Close path edited untracked copies");
    }
}
