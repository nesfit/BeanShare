using BeanShare.Application.Abstractions;
using BeanShare.Application.Features.Balances.Services;
using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Aggregates.Ledger;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using NSubstitute;

namespace BeanShare.Application.Tests.TestHelpers;

/// <summary>A <see cref="LedgerSnapshotService"/> over a space with no cups, entries, bags or settlements.</summary>
public static class EmptyLedger
{
    public static LedgerSnapshotService SnapshotService(IClock clock, ISettlementRepository? settlements = null)
    {
        var consumptions = Substitute.For<IConsumptionRepository>();
        consumptions.GetBySpecAsync(Arg.Any<ISpec<ConsumptionEntry>>(), Arg.Any<CancellationToken>()).Returns(new List<ConsumptionEntry>());
        consumptions.GetBySpecForUpdateAsync(Arg.Any<ISpec<ConsumptionEntry>>(), Arg.Any<CancellationToken>()).Returns(new List<ConsumptionEntry>());

        var entries = Substitute.For<IBalanceEntryRepository>();
        entries.GetUnincludedForUpdateAsync(Arg.Any<SpaceId>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(new List<BalanceEntry>());

        var periods = Substitute.For<IBillingPeriodRepository>();
        periods.GetBySpaceIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>()).Returns(new List<BillingPeriod>());

        return new LedgerSnapshotService(
            consumptions,
            entries,
            settlements ?? Substitute.For<ISettlementRepository>(),
            Substitute.For<ICoffeeStockRepository>(),
            periods,
            clock,
            TestUsers.ActiveRepository());
    }
}
