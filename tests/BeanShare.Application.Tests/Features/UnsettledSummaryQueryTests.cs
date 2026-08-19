using BeanShare.Application.Abstractions;
using BeanShare.Application.Features.Settlement.Queries.GetUnsettledSummary;
using BeanShare.Application.Services;
using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace BeanShare.Application.Tests.Features;

/// <summary>
/// "What do I owe since the last settlement?" — the tally must ignore anything a settlement has
/// already covered, and must not lose cups that were logged outside a billing period.
/// </summary>
public sealed class UnsettledSummaryQueryTests
{
    private readonly ISpaceRepository _spaceRepository = Substitute.For<ISpaceRepository>();
    private readonly IBillingPeriodRepository _billingPeriodRepository = Substitute.For<IBillingPeriodRepository>();
    private readonly IConsumptionRepository _consumptionRepository = Substitute.For<IConsumptionRepository>();
    private readonly ICoffeeStockRepository _coffeeStockRepository = Substitute.For<ICoffeeStockRepository>();
    private readonly IUserService _userService = Substitute.For<IUserService>();
    private readonly IUserContext _userContext = Substitute.For<IUserContext>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private readonly UserId _adminUserId = new(Guid.NewGuid());
    private readonly UserId _memberUserId = new(Guid.NewGuid());
    private readonly SpaceId _spaceId = SpaceId.New();
    private readonly Space _space;

    private static readonly DateTime SettledPeriodStart = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SettledPeriodEnd = new(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc);
    private static readonly DateTime AfterSettlement = new(2026, 7, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime BeforeSettlement = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Now = new(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc);

    public UnsettledSummaryQueryTests()
    {
        _clock.UtcNow.Returns(Now);
        _userContext.CurrentUserId.Returns(_adminUserId);

        _space = Space.Create(_spaceId, "Test Space", Currency.USD, _adminUserId, new InviteCode("TESTAB"), _clock);
        _space.Join(_memberUserId, _clock);

        var users = new List<User>
        {
            User.CreateWithIdAndPassword(_adminUserId.Value, "admin@test.dev", "Admin", "hash", Now),
            User.CreateWithIdAndPassword(_memberUserId.Value, "member@test.dev", "Member", "hash", Now)
        };

        _spaceRepository.GetByIdAsync(Arg.Any<SpaceId>(), default).Returns(_space);
        _userService.GetByIdsAsync(Arg.Any<IEnumerable<UserId>>(), default).Returns(users);
    }

    [Fact]
    public async Task Handle_ShouldIgnoreCupsCoveredByASettlement()
    {
        var settled = CreateSettledPeriod();

        SetupConsumptions(
            Cup(_adminUserId, BeforeSettlement, 18m, 0.50m, settled.Id),
            Cup(_adminUserId, AfterSettlement, 18m, 0.50m),
            Cup(_adminUserId, AfterSettlement, 18m, 0.50m));

        var summary = await BuildSummaryAsync(settled);

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
        var settled = CreateSettledPeriod();

        // Back-dated into an already-settled window but never assigned, so no settlement billed
        // it — it still belongs on the next one.
        SetupConsumptions(
            Cup(_adminUserId, BeforeSettlement, 18m, 0.50m),
            Cup(_adminUserId, AfterSettlement, 18m, 0.50m));

        var summary = await BuildSummaryAsync(settled);

        summary.MyCups.Should().Be(2);
        summary.MyCost.Should().Be(1.00m);
    }

    [Fact]
    public async Task Handle_WhenSpaceWasNeverSettled_ShouldCountEverything()
    {
        SetupConsumptions(
            Cup(_adminUserId, BeforeSettlement, 18m, 0.50m),
            Cup(_memberUserId, AfterSettlement, 18m, 0.50m));

        var summary = await BuildSummaryAsync();

        summary.SettledThrough.Should().BeNull();
        summary.HasEverBeenSettled.Should().BeFalse();
        summary.MyCups.Should().Be(1);
        summary.TotalCups.Should().Be(2);
        summary.TotalCost.Should().Be(1.00m);
    }

    [Fact]
    public async Task Handle_ForPlainMember_ShouldNotExposeOtherMembers()
    {
        _userContext.CurrentUserId.Returns(_memberUserId);

        SetupConsumptions(
            Cup(_adminUserId, AfterSettlement, 18m, 0.50m),
            Cup(_memberUserId, AfterSettlement, 18m, 0.50m));

        var summary = await BuildSummaryAsync();

        summary.IsAdmin.Should().BeFalse();
        summary.Members.Should().BeEmpty();
        summary.MyCups.Should().Be(1);
        summary.MyCost.Should().Be(0.50m);
    }

    [Fact]
    public async Task Handle_ForAdmin_ShouldBreakDownWhoOwesWhat()
    {
        SetupConsumptions(
            Cup(_memberUserId, AfterSettlement, 18m, 0.50m),
            Cup(_memberUserId, AfterSettlement, 18m, 0.50m),
            Cup(_adminUserId, AfterSettlement, 18m, 0.50m));

        // The admin bought the bag everyone is drinking, so they are the space's creditor.
        SetupStock(purchasedBy: _adminUserId, grams: 1000m, cost: 20.00m, purchasedAt: AfterSettlement);

        var summary = await BuildSummaryAsync();

        summary.IsAdmin.Should().BeTrue();
        summary.Members.Should().HaveCount(2);

        var memberRow = summary.Members.First(m => m.UserId == _memberUserId.Value);
        memberRow.UserName.Should().Be("Member");
        memberRow.Cups.Should().Be(2);
        memberRow.Cost.Should().Be(1.00m);
        memberRow.Paid.Should().Be(0m);
        memberRow.Balance.Should().Be(-1.00m);
        memberRow.IsCurrentUser.Should().BeFalse();

        var adminRow = summary.Members.First(m => m.UserId == _adminUserId.Value);
        adminRow.Paid.Should().Be(20.00m);
        adminRow.Balance.Should().Be(19.50m);
        adminRow.IsCurrentUser.Should().BeTrue();

        summary.MyPaid.Should().Be(20.00m);
        summary.MyBalance.Should().Be(19.50m);
    }

    [Fact]
    public async Task Handle_ForLegacyCupsWithoutAStampedCost_ShouldFallBackToWeightedAverage()
    {
        SetupConsumptions(Cup(_adminUserId, AfterSettlement, 20m, cost: null));
        SetupStock(purchasedBy: _adminUserId, grams: 1000m, cost: 20.00m, purchasedAt: AfterSettlement);

        var summary = await BuildSummaryAsync();

        // 20 g at 20.00 / 1000 g
        summary.MyCost.Should().Be(0.40m);
    }

    [Fact]
    public async Task Handle_WithoutASpaceId_ShouldCoverEverySpaceTheCallerBelongsTo()
    {
        _spaceRepository.GetBySpecAsync(Arg.Any<ISpec<Space>>(), default)
            .Returns(new List<Space> { _space });

        SetupConsumptions(Cup(_adminUserId, AfterSettlement, 18m, 0.50m));

        var result = await CreateHandler().Handle(new GetUnsettledSummaryQuery(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Spaces.Should().HaveCount(1);
        result.Value.ActiveSpaces.Should().HaveCount(1);
        result.Value.MyCups.Should().Be(1);
        result.Value.Spaces[0].SpaceName.Should().Be("Test Space");
    }

    [Fact]
    public async Task Handle_ForANonMember_ShouldFail()
    {
        _userContext.CurrentUserId.Returns(new UserId(Guid.NewGuid()));

        var result = await CreateHandler().Handle(new GetUnsettledSummaryQuery(_spaceId.Value), default);

        result.IsSuccess.Should().BeFalse();
        result.Errors[0].Code.Should().Be("INSUFFICIENT_PRIVILEGES");
    }

    [Fact]
    public async Task Handle_WhenSpaceDoesNotExist_ShouldFail()
    {
        _spaceRepository.GetByIdAsync(Arg.Any<SpaceId>(), default).Returns((Space?)null);

        var result = await CreateHandler().Handle(new GetUnsettledSummaryQuery(Guid.NewGuid()), default);

        result.IsSuccess.Should().BeFalse();
        result.Errors[0].Code.Should().Be("SPACE_NOT_FOUND");
    }

    private async Task<Application.Features.Settlement.Dtos.UnsettledSpaceSummaryDto> BuildSummaryAsync(
        params BillingPeriod[] periods)
    {
        _billingPeriodRepository.GetBySpaceIdAsync(Arg.Any<SpaceId>(), default).Returns(periods);

        var result = await CreateHandler().Handle(new GetUnsettledSummaryQuery(_spaceId.Value), default);

        result.IsSuccess.Should().BeTrue();
        return result.Value.Spaces.Single();
    }

    private BillingPeriod CreateSettledPeriod()
    {
        var period = BillingPeriod.Create(_spaceId, "June 2026", SettledPeriodStart, SettledPeriodEnd, _adminUserId, _clock);
        period.Open(_adminUserId, _clock);
        period.Close(_adminUserId, _clock);
        period.MarkAsSettled(_adminUserId, _clock);
        return period;
    }

    private void SetupConsumptions(params ConsumptionEntry[] entries) =>
        _consumptionRepository.GetBySpecAsync(Arg.Any<ISpec<ConsumptionEntry>>(), default).Returns(entries);

    private void SetupStock(UserId purchasedBy, decimal grams, decimal cost, DateTime purchasedAt)
    {
        var stock = CoffeeStock.Create(_spaceId, _clock);
        stock.AddPurchase(
            CoffeeProduct.Create("Test Coffee", "Test Brand", CoffeeType.Espresso),
            Weight.FromGrams(grams),
            Money.Create(cost, "USD"),
            "Test Vendor",
            purchasedBy,
            purchasedAt,
            _clock);

        _coffeeStockRepository.GetBySpaceIdAsync(Arg.Any<SpaceId>(), default).Returns(stock);
    }

    private ConsumptionEntry Cup(
        UserId userId,
        DateTime consumedAt,
        decimal grams,
        decimal? cost,
        BillingPeriodId? billingPeriodId = null)
    {
        var entry = ConsumptionEntry.Create(
            _spaceId,
            userId,
            CoffeeProduct.Create("Test Coffee", "Test Brand", CoffeeType.Espresso),
            Weight.FromGrams(grams),
            consumedAt,
            _clock,
            cost: cost.HasValue ? Money.Create(cost.Value, "USD") : null);

        if (billingPeriodId.HasValue)
        {
            entry.AssignToBillingPeriod(billingPeriodId.Value);
        }

        return entry;
    }

    private GetUnsettledSummaryHandler CreateHandler() => new(
        _spaceRepository,
        _billingPeriodRepository,
        _consumptionRepository,
        _coffeeStockRepository,
        _userService,
        _userContext);
}
