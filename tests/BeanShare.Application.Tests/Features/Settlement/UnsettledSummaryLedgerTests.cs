using BeanShare.Application.Features.Settlement.Dtos;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Common;
using NSubstitute;

namespace BeanShare.Application.Tests.Features.Settlement;

// T-QRY-01: the unsettled card shows the running balance, carry + adjustments + credit − cost (§5.3).
public sealed class UnsettledSummaryLedgerTests
{
    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Jan31 = new(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb10 = new(2026, 2, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly LedgerWorld _world = new("CZK", new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc));
    private readonly UserId _alice;
    private readonly UserId _bob;

    public UnsettledSummaryLedgerTests()
    {
        _alice = _world.AddMember("Alice");
        _bob = _world.AddMember("Bob");
        _world.Space.SetTreasurer(_world.Admin);
    }

    [Fact]
    public async Task TheRunningBalance_IsCarryPlusAdjustmentsPlusCreditMinusCost()
    {
        _world.AddEntry(_alice, -100m, Jan1);
        await _world.GenerateAsync(_world.AddPeriod("January", Jan1, Jan31));

        var bag = _world.AddBag(1000m, 500m, Feb10.AddDays(-1), paidBy: _alice);
        _world.Drink(_alice, 10m, Feb10, bag);
        _world.Drink(_bob, 30m, Feb10, bag);
        _world.AddEntry(_alice, 20m, Feb10, kind: Domain.Enums.BalanceEntryKind.Correction, note: "refund");
        _world.Actor = _alice;

        var summary = await _world.UnsettledAsync();

        summary.MyCarried.Should().Be(-100m);
        summary.MyAdjustments.Should().Be(20m);
        summary.MyCredit.Should().Be(20m);
        summary.MyCost.Should().Be(5m);
        summary.MyBalance.Should().Be(-100m + 20m + 20m - 5m);
        summary.Members.Should().BeEmpty("a member sees only their own numbers");
    }

    [Fact]
    public async Task AdminRows_SumToZeroAndIncludeEntryPartiesAndCarryHolders()
    {
        _world.AddEntry(_bob, 40m, Jan1);
        await _world.GenerateAsync(_world.AddPeriod("January", Jan1, Jan31));
        _world.AddEntry(_alice, -15m, Feb10);

        var summary = await _world.UnsettledAsync();

        summary.Members.Select(m => m.UserId).Should().BeEquivalentTo(new[] { _alice.Value, _bob.Value, _world.Admin.Value });
        summary.Members.Single(m => m.UserId == _bob.Value).Carried.Should().Be(40m);
        summary.Members.Single(m => m.UserId == _alice.Value).Balance.Should().Be(-15m);
        summary.Members.Sum(m => m.Balance).Should().Be(0m);
    }

    [Fact]
    public async Task ASpaceWithBalancesButNoCups_IsActive()
    {
        _world.AddEntry(_alice, -15m, Feb10);
        var summary = await _world.UnsettledAsync();

        new UnsettledSummaryResult([summary]).ActiveSpaces.Should().ContainSingle();
        summary.TotalCups.Should().Be(0);
    }

    // Code round 1, F1: the running balance asks the database only for unsettled candidates.
    [Fact]
    public async Task TheRunningBalance_DoesNotLoadCupsOfSettledPeriods()
    {
        var january = _world.AddPeriod("January", Jan1, Jan31, Domain.Enums.BillingState.Settled);
        var settledCup = _world.LegacyCup(_alice, 10m, Jan1.AddDays(5));
        settledCup.AssignToBillingPeriod(january.Id);
        var openCup = _world.LegacyCup(_alice, 10m, Feb10);
        Domain.Specifications.ISpec<Domain.Entities.ConsumptionEntry>? asked = null;
        _world.ConsumptionRepository
            .When(r => r.GetBySpecAsync(Arg.Any<Domain.Specifications.ISpec<Domain.Entities.ConsumptionEntry>>(), Arg.Any<CancellationToken>()))
            .Do(ci => asked = ci.Arg<Domain.Specifications.ISpec<Domain.Entities.ConsumptionEntry>>());

        var summary = await _world.UnsettledAsync();

        summary.TotalCups.Should().Be(1);
        var criteria = asked!.Criteria.Compile();
        criteria(settledCup).Should().BeFalse();
        criteria(openCup).Should().BeTrue();
    }

    // Code round 1, F5: without a treasurer the rounding residual is shown, so the balances still net to zero.
    [Fact]
    public async Task WithoutATreasurer_TheRoundingResidualIsShownSoBalancesNetToZero()
    {
        _world.Space.SetTreasurer(null);
        var carol = _world.AddMember("Carol");
        _world.AddBag(30m, 100.00m, Feb10.AddDays(-1), paidBy: _bob);
        _world.LegacyCup(_alice, 10m, Feb10);
        _world.LegacyCup(carol, 10m, Feb10);
        _world.LegacyCup(_world.Admin, 10m, Feb10);

        var summary = await _world.UnsettledAsync();

        summary.Members.Sum(m => m.Balance).Should().Be(0.01m, "three costs of 33.33 against a credit of 100.00");
        summary.UnassignedRounding.Should().Be(-0.01m);
        (summary.Members.Sum(m => m.Balance) + summary.UnassignedRounding).Should().Be(0m);
    }
}
