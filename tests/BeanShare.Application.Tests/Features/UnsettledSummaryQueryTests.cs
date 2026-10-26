using BeanShare.Application.Features.Settlement.Queries.GetUnsettledSummary;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace BeanShare.Application.Tests.Features;

/// <summary>
/// "What do I owe since the last settlement?" — the tally must ignore anything a settlement has
/// already covered, must not lose cups that were logged outside a billing period, and shows each
/// member's running balance from the settlement calculator (design-tests §11.2).
/// </summary>
public sealed class UnsettledSummaryQueryTests
{
    private static readonly DateTime SettledPeriodStart = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SettledPeriodEnd = new(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AfterSettlement = new(2026, 7, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime BeforeSettlement = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc);

    private readonly LedgerWorld _world = new("USD", Now);
    private readonly UserId _member;

    public UnsettledSummaryQueryTests()
    {
        _member = _world.AddMember("Member");
        _world.Space.SetTreasurer(_world.Admin);
    }

    [Fact]
    public async Task Handle_ShouldIgnoreCupsCoveredByASettlement()
    {
        var settled = _world.AddPeriod("June 2026", SettledPeriodStart, SettledPeriodEnd, BillingState.Settled);
        _world.StampedCup(_world.Admin, 18m, BeforeSettlement, 0.50m).AssignToBillingPeriod(settled.Id);
        _world.StampedCup(_world.Admin, 18m, AfterSettlement, 0.50m);
        _world.StampedCup(_world.Admin, 18m, AfterSettlement, 0.50m);

        var summary = await _world.UnsettledAsync();

        summary.MyCups.Should().Be(2);
        summary.MyGrams.Should().Be(36m);
        summary.MyCost.Should().Be(1.00m);
        summary.SettledThrough.Should().Be(settled.EndDate);
        summary.LastSettledPeriodName.Should().Be("June 2026");
        summary.HasEverBeenSettled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldCountCupsThatNeverGotABillingPeriod()
    {
        _world.AddPeriod("June 2026", SettledPeriodStart, SettledPeriodEnd, BillingState.Settled);

        // Back-dated into an already-settled window but never stamped, so no settlement billed
        // it — it still belongs on the next one.
        _world.StampedCup(_world.Admin, 18m, BeforeSettlement, 0.50m);
        _world.StampedCup(_world.Admin, 18m, AfterSettlement, 0.50m);

        var summary = await _world.UnsettledAsync();

        summary.MyCups.Should().Be(2);
        summary.MyCost.Should().Be(1.00m);
    }

    [Fact]
    public async Task Handle_ShouldSkipCupsBeforeTheLedgerStart()
    {
        _world.Space.SetLedgerStartDate(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), hasLedgerSettlement: false);
        _world.StampedCup(_world.Admin, 18m, BeforeSettlement, 0.50m);
        _world.StampedCup(_world.Admin, 18m, AfterSettlement, 0.50m);

        var summary = await _world.UnsettledAsync();

        summary.MyCups.Should().Be(1);
        summary.MyCost.Should().Be(0.50m);
    }

    [Fact]
    public async Task Handle_WhenSpaceWasNeverSettled_ShouldCountEverything()
    {
        _world.StampedCup(_world.Admin, 18m, BeforeSettlement, 0.50m);
        _world.StampedCup(_member, 18m, AfterSettlement, 0.50m);

        var summary = await _world.UnsettledAsync();

        summary.SettledThrough.Should().BeNull();
        summary.HasEverBeenSettled.Should().BeFalse();
        summary.MyCups.Should().Be(1);
        summary.TotalCups.Should().Be(2);
        summary.TotalCost.Should().Be(1.00m);
    }

    [Fact]
    public async Task Handle_ForPlainMember_ShouldNotExposeOtherMembers()
    {
        _world.Actor = _member;
        _world.StampedCup(_world.Admin, 18m, AfterSettlement, 0.50m);
        _world.StampedCup(_member, 18m, AfterSettlement, 0.50m);

        var summary = await _world.UnsettledAsync();

        summary.IsAdmin.Should().BeFalse();
        summary.Members.Should().BeEmpty();
        summary.MyCups.Should().Be(1);
        summary.MyCost.Should().Be(0.50m);
    }

    [Fact]
    public async Task Handle_ForAdmin_ShouldBreakDownWhoOwesWhat()
    {
        // The admin bought the bag everyone is drinking, so they are the space's creditor.
        var bag = _world.AddBag(1000m, 20.00m, AfterSettlement.AddDays(-1));
        _world.Drink(_member, 18m, AfterSettlement, bag);
        _world.Drink(_member, 18m, AfterSettlement, bag);
        _world.Drink(_world.Admin, 18m, AfterSettlement, bag);

        var summary = await _world.UnsettledAsync();

        summary.IsAdmin.Should().BeTrue();
        summary.Members.Should().HaveCount(2);

        var memberRow = summary.Members.First(m => m.UserId == _member.Value);
        memberRow.UserName.Should().Be("Member");
        memberRow.Cups.Should().Be(2);
        memberRow.Cost.Should().Be(0.72m);
        memberRow.Credit.Should().Be(0m);
        memberRow.Paid.Should().Be(0m);
        memberRow.Balance.Should().Be(-0.72m);
        memberRow.IsCurrentUser.Should().BeFalse();

        var adminRow = summary.Members.First(m => m.UserId == _world.Admin.Value);
        adminRow.Paid.Should().Be(20.00m, "Paid stays as an informational figure");
        adminRow.Credit.Should().Be(1.08m);
        adminRow.Balance.Should().Be(0.72m, "the balance is credit for cups drunk from the bag, not the bag's price");
        adminRow.IsCurrentUser.Should().BeTrue();

        summary.MyPaid.Should().Be(20.00m);
        summary.MyCredit.Should().Be(1.08m);
        summary.MyBalance.Should().Be(0.72m);
    }

    [Fact]
    public async Task Handle_ForLegacyCupsWithoutAStampedCost_ShouldFallBackToTheRateAsOfNow()
    {
        _world.LegacyCup(_world.Admin, 20m, AfterSettlement);
        _world.AddBag(1000m, 20.00m, AfterSettlement);

        var summary = await _world.UnsettledAsync();

        // 20 g at 20.00 / 1000 g
        summary.MyCost.Should().Be(0.40m);
    }

    [Fact]
    public async Task Handle_WithoutASpaceId_ShouldCoverEverySpaceTheCallerBelongsTo()
    {
        _world.SpaceRepository.GetBySpecAsync(Arg.Any<ISpec<Space>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Space> { _world.Space });
        _world.StampedCup(_world.Admin, 18m, AfterSettlement, 0.50m);

        var result = await new GetUnsettledSummaryHandler(_world.SpaceRepository, _world.BillingPeriodRepository,
            _world.SnapshotService, _world.UserService, _world.UserContext).Handle(new GetUnsettledSummaryQuery(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Spaces.Should().HaveCount(1);
        result.Value.ActiveSpaces.Should().HaveCount(1);
        result.Value.MyCups.Should().Be(1);
        result.Value.Spaces[0].SpaceName.Should().Be("Kava");
    }

    [Fact]
    public async Task Handle_ForANonMember_ShouldFail()
    {
        _world.Actor = UserId.New();

        var result = await new GetUnsettledSummaryHandler(_world.SpaceRepository, _world.BillingPeriodRepository,
            _world.SnapshotService, _world.UserService, _world.UserContext).Handle(new GetUnsettledSummaryQuery(_world.Space.Id.Value), default);

        result.IsSuccess.Should().BeFalse();
        result.Errors[0].Code.Should().Be("INSUFFICIENT_PRIVILEGES");
    }

    [Fact]
    public async Task Handle_WhenSpaceDoesNotExist_ShouldFail()
    {
        _world.SpaceRepository.GetByIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>()).Returns((Space?)null);

        var result = await new GetUnsettledSummaryHandler(_world.SpaceRepository, _world.BillingPeriodRepository,
            _world.SnapshotService, _world.UserService, _world.UserContext).Handle(new GetUnsettledSummaryQuery(Guid.NewGuid()), default);

        result.IsSuccess.Should().BeFalse();
        result.Errors[0].Code.Should().Be("SPACE_NOT_FOUND");
    }
}
