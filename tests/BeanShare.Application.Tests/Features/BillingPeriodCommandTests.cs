using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Billing.Commands.CloseBillingPeriod;
using BeanShare.Application.Features.Billing.Commands.CreateBillingPeriod;
using BeanShare.Application.Features.Billing.Commands.OpenBillingPeriod;
using BeanShare.Application.Features.Billing.Commands.DeleteBillingPeriod;
using BeanShare.Application.Features.Billing.Commands.ReopenBillingPeriod;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace BeanShare.Application.Tests.Features;

public sealed class BillingPeriodCommandTests
{
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    private readonly UserId _adminUserId;
    private readonly UserId _memberUserId;
    private readonly SpaceId _spaceId;
    private readonly Space _space;
    private readonly DateTime _fixedNow;

    public BillingPeriodCommandTests()
    {
        _billingPeriodRepository = Substitute.For<IBillingPeriodRepository>();
        _spaceRepository = Substitute.For<ISpaceRepository>();
        _consumptionRepository = Substitute.For<IConsumptionRepository>();
        _userContext = Substitute.For<IUserContext>();
        _clock = Substitute.For<IClock>();

        _adminUserId = new UserId(Guid.NewGuid());
        _memberUserId = new UserId(Guid.NewGuid());
        _spaceId = SpaceId.New();
        _fixedNow = new DateTime(2025, 10, 26, 10, 0, 0, DateTimeKind.Utc);

        _clock.UtcNow.Returns(_fixedNow);
        _userContext.CurrentUserId.Returns(_adminUserId);

        _space = Space.Create(_spaceId, "Test Space", Currency.USD, _adminUserId, new InviteCode("TESTAB"), _clock);
        _space.Join(_memberUserId, _clock);
    }

    [Fact]
    public async Task CreateBillingPeriod_WithValidData_ShouldSucceed()
    {
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(_space);
        _billingPeriodRepository.HasOverlappingPeriodAsync(
            Arg.Any<SpaceId>(),
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            Arg.Any<BillingPeriodId?>(),
            default).Returns(false);

        var handler = new CreateBillingPeriodHandler(_billingPeriodRepository, _spaceRepository, _userContext, _clock);
        var command = new CreateBillingPeriodCommand(
            _spaceId.Value,
            "October 2025",
            new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 10, 31, 0, 0, 0, DateTimeKind.Utc));

        var result = await handler.Handle(command, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("October 2025");
        result.Value.State.Should().Be("Draft");
        result.Value.SpaceId.Should().Be(_spaceId.Value);
        result.Value.StartDate.Should().Be(new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        result.Value.EndDate.Should().Be(new DateTime(2025, 10, 31, 23, 59, 59, 999, DateTimeKind.Utc).AddTicks(9999));
        await _billingPeriodRepository.Received(1).AddAsync(Arg.Any<BillingPeriod>(), default);
    }

    [Fact]
    public async Task CreateBillingPeriod_WhenSpaceNotFound_ShouldFail()
    {
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns((Space?)null);

        var handler = new CreateBillingPeriodHandler(_billingPeriodRepository, _spaceRepository, _userContext, _clock);
        var command = new CreateBillingPeriodCommand(
            Guid.NewGuid(),
            "Test Period",
            new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 10, 31, 0, 0, 0, DateTimeKind.Utc));

        var result = await handler.Handle(command, default);

        result.IsSuccess.Should().BeFalse();
        result.Errors[0].Code.Should().Be("SPACE_NOT_FOUND");
        await _billingPeriodRepository.DidNotReceive().AddAsync(Arg.Any<BillingPeriod>(), default);
    }

    [Fact]
    public async Task CreateBillingPeriod_WhenUserNotAdmin_ShouldFail()
    {
        _userContext.CurrentUserId.Returns(_memberUserId);
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(_space);

        var handler = new CreateBillingPeriodHandler(_billingPeriodRepository, _spaceRepository, _userContext, _clock);
        var command = new CreateBillingPeriodCommand(
            _spaceId.Value,
            "Test Period",
            new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 10, 31, 0, 0, 0, DateTimeKind.Utc));

        var result = await handler.Handle(command, default);

        result.IsSuccess.Should().BeFalse();
        result.Errors[0].Code.Should().Be("INSUFFICIENT_PRIVILEGES");
        await _billingPeriodRepository.DidNotReceive().AddAsync(Arg.Any<BillingPeriod>(), default);
    }

    [Fact]
    public async Task CreateBillingPeriod_WithOverlappingPeriod_ShouldFail()
    {
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(_space);
        _billingPeriodRepository.HasOverlappingPeriodAsync(
            Arg.Any<SpaceId>(),
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            Arg.Any<BillingPeriodId?>(),
            default).Returns(true);

        var handler = new CreateBillingPeriodHandler(_billingPeriodRepository, _spaceRepository, _userContext, _clock);
        var command = new CreateBillingPeriodCommand(
            _spaceId.Value,
            "Test Period",
            new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 10, 31, 0, 0, 0, DateTimeKind.Utc));

        var result = await handler.Handle(command, default);

        result.IsSuccess.Should().BeFalse();
        result.Errors[0].Code.Should().Be("BILLING_PERIOD_OVERLAP");
        await _billingPeriodRepository.DidNotReceive().AddAsync(Arg.Any<BillingPeriod>(), default);
    }

    [Fact]
    public async Task OpenBillingPeriod_WithDraftPeriod_ShouldSucceed()
    {
        var billingPeriod = BillingPeriod.Create(
            _spaceId,
            "Test Period",
            new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 10, 31, 0, 0, 0, DateTimeKind.Utc),
            _adminUserId,
            _clock);

        _billingPeriodRepository.GetByIdAsync(Arg.Any<BillingPeriodId>(), default).Returns(billingPeriod);
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(_space);

        var handler = new OpenBillingPeriodHandler(_billingPeriodRepository, _spaceRepository, _userContext, _clock);
        var command = new OpenBillingPeriodCommand(billingPeriod.Id.Value);

        var result = await handler.Handle(command, default);

        result.IsSuccess.Should().BeTrue();
        billingPeriod.State.Should().Be(BillingState.Open);
        await _billingPeriodRepository.Received(1).UpdateAsync(billingPeriod, default);
    }

    [Fact]
    public async Task OpenBillingPeriod_WhenPeriodNotFound_ShouldFail()
    {
        _billingPeriodRepository.GetByIdAsync(Arg.Any<BillingPeriodId>(), default).Returns((BillingPeriod?)null);

        var handler = new OpenBillingPeriodHandler(_billingPeriodRepository, _spaceRepository, _userContext, _clock);
        var command = new OpenBillingPeriodCommand(Guid.NewGuid());

        var result = await handler.Handle(command, default);

        result.IsSuccess.Should().BeFalse();
        result.Errors[0].Code.Should().Be("BILLING_PERIOD_NOT_FOUND");
    }

    [Fact]
    public async Task OpenBillingPeriod_WhenUserNotAdmin_ShouldFail()
    {
        var billingPeriod = BillingPeriod.Create(
            _spaceId,
            "Test Period",
            new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 10, 31, 0, 0, 0, DateTimeKind.Utc),
            _adminUserId,
            _clock);

        _userContext.CurrentUserId.Returns(_memberUserId);
        _billingPeriodRepository.GetByIdAsync(Arg.Any<BillingPeriodId>(), default).Returns(billingPeriod);
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(_space);

        var handler = new OpenBillingPeriodHandler(_billingPeriodRepository, _spaceRepository, _userContext, _clock);
        var command = new OpenBillingPeriodCommand(billingPeriod.Id.Value);

        var result = await handler.Handle(command, default);

        result.IsSuccess.Should().BeFalse();
        result.Errors[0].Code.Should().Be("INSUFFICIENT_PRIVILEGES");
    }

    [Fact]
    public async Task OpenBillingPeriod_WhenPeriodAlreadyOpen_ShouldFail()
    {
        var billingPeriod = BillingPeriod.Create(
            _spaceId,
            "Test Period",
            new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 10, 31, 0, 0, 0, DateTimeKind.Utc),
            _adminUserId,
            _clock);

        billingPeriod.Open(_adminUserId, _clock);

        _billingPeriodRepository.GetByIdAsync(Arg.Any<BillingPeriodId>(), default).Returns(billingPeriod);
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(_space);

        var handler = new OpenBillingPeriodHandler(_billingPeriodRepository, _spaceRepository, _userContext, _clock);
        var command = new OpenBillingPeriodCommand(billingPeriod.Id.Value);

        var result = await handler.Handle(command, default);

        result.IsSuccess.Should().BeFalse();
        result.Errors[0].Code.Should().Be("DOMAIN_ERROR");
    }

    [Fact]
    public async Task CloseBillingPeriod_WithOpenPeriod_ShouldSucceedAndAssignConsumptions()
    {
        var billingPeriod = BillingPeriod.Create(
            _spaceId,
            "Test Period",
            new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 10, 31, 0, 0, 0, DateTimeKind.Utc),
            _adminUserId,
            _clock);

        billingPeriod.Open(_adminUserId, _clock);

        var consumptions = new List<ConsumptionEntry>
        {
            CreateConsumption(new DateTime(2025, 10, 15, 9, 0, 0, DateTimeKind.Utc)),
            CreateConsumption(new DateTime(2025, 10, 20, 14, 0, 0, DateTimeKind.Utc)),
            CreateConsumption(new DateTime(2025, 9, 30, 9, 0, 0, DateTimeKind.Utc))
        };

        _billingPeriodRepository.GetByIdAsync(Arg.Any<BillingPeriodId>(), default).Returns(billingPeriod);
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(_space);
        _consumptionRepository.GetBySpecForUpdateAsync(Arg.Any<ISpec<ConsumptionEntry>>(), default).Returns(consumptions);

        var handler = new CloseBillingPeriodHandler(
            _billingPeriodRepository,
            _spaceRepository,
            _consumptionRepository,
            _userContext,
            _clock);

        var command = new CloseBillingPeriodCommand(billingPeriod.Id.Value);

        var result = await handler.Handle(command, default);

        result.IsSuccess.Should().BeTrue();
        billingPeriod.State.Should().Be(BillingState.Closed);
        consumptions.Where(c => c.ConsumedAt >= billingPeriod.StartDate && c.ConsumedAt <= billingPeriod.EndDate)
            .Should().AllSatisfy(c => c.BillingPeriodId.Should().NotBeNull());
        await _billingPeriodRepository.Received(1).UpdateAsync(billingPeriod, default);
    }

    [Fact]
    public async Task CloseBillingPeriod_WhenPeriodNotOpen_ShouldFail()
    {
        var billingPeriod = BillingPeriod.Create(
            _spaceId,
            "Test Period",
            new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 10, 31, 0, 0, 0, DateTimeKind.Utc),
            _adminUserId,
            _clock);

        _billingPeriodRepository.GetByIdAsync(Arg.Any<BillingPeriodId>(), default).Returns(billingPeriod);
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(_space);
        _consumptionRepository.GetBySpecForUpdateAsync(Arg.Any<ISpec<ConsumptionEntry>>(), default).Returns(new List<ConsumptionEntry>());

        var handler = new CloseBillingPeriodHandler(
            _billingPeriodRepository,
            _spaceRepository,
            _consumptionRepository,
            _userContext,
            _clock);

        var command = new CloseBillingPeriodCommand(billingPeriod.Id.Value);

        var result = await handler.Handle(command, default);

        result.IsSuccess.Should().BeFalse();
        result.Errors[0].Code.Should().Be("INVALID_BILLING_PERIOD_STATE");
        billingPeriod.State.Should().Be(BillingState.Draft);
    }

    [Fact]
    public async Task CloseBillingPeriod_ShouldOnlyAssignUnassignedConsumptions()
    {
        var billingPeriod = BillingPeriod.Create(
            _spaceId,
            "Test Period",
            new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 10, 31, 0, 0, 0, DateTimeKind.Utc),
            _adminUserId,
            _clock);

        billingPeriod.Open(_adminUserId, _clock);

        var otherPeriodId = new BillingPeriodId(Guid.NewGuid());
        var consumption1 = CreateConsumption(new DateTime(2025, 10, 15, 9, 0, 0, DateTimeKind.Utc));
        var consumption2 = CreateConsumption(new DateTime(2025, 10, 20, 14, 0, 0, DateTimeKind.Utc));
        consumption2.AssignToBillingPeriod(otherPeriodId);

        var unassignedConsumptions = new List<ConsumptionEntry> { consumption1 };

        _billingPeriodRepository.GetByIdAsync(Arg.Any<BillingPeriodId>(), default).Returns(billingPeriod);
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(_space);
        _consumptionRepository.GetBySpecForUpdateAsync(Arg.Any<ISpec<ConsumptionEntry>>(), default).Returns(unassignedConsumptions);

        var handler = new CloseBillingPeriodHandler(
            _billingPeriodRepository,
            _spaceRepository,
            _consumptionRepository,
            _userContext,
            _clock);

        var command = new CloseBillingPeriodCommand(billingPeriod.Id.Value);

        var result = await handler.Handle(command, default);

        result.IsSuccess.Should().BeTrue();
        consumption1.BillingPeriodId.Should().Be(billingPeriod.Id);
        consumption2.BillingPeriodId.Should().Be(otherPeriodId);
    }

    [Fact]
    public async Task OpenBillingPeriod_WithPastEndDate_ShouldSucceed()
    {
        var clockAtDec = Substitute.For<IClock>();
        clockAtDec.UtcNow.Returns(new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc));

        var billingPeriod = BillingPeriod.Create(
            _spaceId,
            "October 2025",
            new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 10, 31, 0, 0, 0, DateTimeKind.Utc),
            _adminUserId,
            _clock);

        _billingPeriodRepository.GetByIdAsync(Arg.Any<BillingPeriodId>(), default).Returns(billingPeriod);
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(_space);

        var handler = new OpenBillingPeriodHandler(_billingPeriodRepository, _spaceRepository, _userContext, clockAtDec);
        var command = new OpenBillingPeriodCommand(billingPeriod.Id.Value);

        var result = await handler.Handle(command, default);

        result.IsSuccess.Should().BeTrue();
        billingPeriod.State.Should().Be(BillingState.Open);
        await _billingPeriodRepository.Received(1).UpdateAsync(billingPeriod, default);
    }

    [Fact]
    public async Task FullHistoricalBillingFlow_OpenClosePastPeriod_ShouldSucceed()
    {
        var clockAtDec = Substitute.For<IClock>();
        clockAtDec.UtcNow.Returns(new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc));

        var billingPeriod = BillingPeriod.Create(
            _spaceId,
            "October 2025",
            new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 10, 31, 0, 0, 0, DateTimeKind.Utc),
            _adminUserId,
            _clock);

        billingPeriod.State.Should().Be(BillingState.Draft);

        billingPeriod.Open(_adminUserId, clockAtDec);
        billingPeriod.State.Should().Be(BillingState.Open);

        billingPeriod.Close(_adminUserId, clockAtDec);
        billingPeriod.State.Should().Be(BillingState.Closed);
        billingPeriod.ClosedAt.Should().Be(new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private ConsumptionEntry CreateConsumption(DateTime consumedAt)
    {
        var product = CoffeeProduct.Create("Test Coffee", "Test Brand", CoffeeType.Espresso);
        return ConsumptionEntry.Create(
            _spaceId,
            _adminUserId,
            product,
            Weight.FromGrams(18m),
            consumedAt,
            _clock);
    }

    // --- T-BIL-01 / T-BIL-02: ordering and concurrency rules of period transitions (B5, DS-25, DS-26) ---

    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Jan31 = new(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb1 = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb28 = new(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc);

    private static LedgerWorld LedgerWorld() => new("CZK", new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task ReopenBillingPeriod_WithALaterPeriod_KeepsTheEndDate()
    {
        var world = LedgerWorld();
        var january = world.AddPeriod("January", Jan1, Jan31);
        world.AddPeriod("February", Feb1, Feb28, BillingState.Draft);
        var end = january.EndDate;
        var versionBefore = january.Version;
        var ledgerVersionBefore = world.Space.LedgerVersion;

        var result = await new ReopenBillingPeriodHandler(world.BillingPeriodRepository, world.SpaceRepository, world.UserContext, world.Clock)
            .Handle(new ReopenBillingPeriodCommand(january.Id.Value), default);

        result.IsSuccess.Should().BeTrue();
        january.State.Should().Be(BillingState.Open);
        january.EndDate.Should().Be(end);
        january.Version.Should().BeGreaterThan(versionBefore);
        world.Space.LedgerVersion.Should().BeGreaterThan(ledgerVersionBefore);
    }

    [Fact]
    public async Task ReopenBillingPeriod_WithoutALaterPeriod_BecomesOpenEnded()
    {
        var world = LedgerWorld();
        var january = world.AddPeriod("January", Jan1, Jan31);

        var result = await new ReopenBillingPeriodHandler(world.BillingPeriodRepository, world.SpaceRepository, world.UserContext, world.Clock)
            .Handle(new ReopenBillingPeriodCommand(january.Id.Value), default);

        result.IsSuccess.Should().BeTrue();
        january.IsOpenEnded.Should().BeTrue();
    }

    [Theory]
    [InlineData(BillingState.Draft, true)]
    [InlineData(BillingState.Open, false)]
    [InlineData(BillingState.Closed, false)]
    [InlineData(BillingState.Settled, false)]
    public async Task DeleteBillingPeriod_AllowsDraftOnly(BillingState state, bool deletable)
    {
        var world = LedgerWorld();
        var period = world.AddPeriod("January", Jan1, Jan31, state);
        var ledgerVersionBefore = world.Space.LedgerVersion;

        var result = await new DeleteBillingPeriodHandler(world.BillingPeriodRepository, world.SpaceRepository, world.UserContext)
            .Handle(new DeleteBillingPeriodCommand(period.Id.Value), default);

        if (deletable)
        {
            result.IsSuccess.Should().BeTrue();
            world.Periods.Should().BeEmpty();
            world.Space.LedgerVersion.Should().BeGreaterThan(ledgerVersionBefore);
        }
        else
        {
            result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.PeriodNotDeletable);
            world.Periods.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task DeleteBillingPeriod_ByANonAdmin_IsRefused()
    {
        var world = LedgerWorld();
        var period = world.AddPeriod("January", Jan1, Jan31, BillingState.Draft);
        world.Actor = world.AddMember("Member");

        var result = await new DeleteBillingPeriodHandler(world.BillingPeriodRepository, world.SpaceRepository, world.UserContext)
            .Handle(new DeleteBillingPeriodCommand(period.Id.Value), default);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.InsufficientPrivileges);
    }

    [Fact]
    public async Task CreateBillingPeriod_StartingOnOrBeforeTheLastSettledEnd_IsRefused()
    {
        var world = LedgerWorld();
        world.AddPeriod("January", Jan1, Jan31, BillingState.Settled);
        var handler = new CreateBillingPeriodHandler(world.BillingPeriodRepository, world.SpaceRepository, world.UserContext, world.Clock);

        var backDated = await handler.Handle(new CreateBillingPeriodCommand(world.Space.Id.Value, "Late", Jan31, Feb28), default);
        var afterwards = await handler.Handle(new CreateBillingPeriodCommand(world.Space.Id.Value, "February", Feb1, Feb28), default);

        backDated.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.SettlementOrder);
        afterwards.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task OpenBillingPeriod_OfADraftStartingBeforeTheLastSettledEnd_IsRefused()
    {
        var world = LedgerWorld();
        var draft = world.AddPeriod("Old draft", Jan1, Jan1.AddDays(9), BillingState.Draft);
        world.AddPeriod("Later", Jan1.AddDays(10), Jan31, BillingState.Settled);

        var result = await new OpenBillingPeriodHandler(world.BillingPeriodRepository, world.SpaceRepository, world.UserContext, world.Clock)
            .Handle(new OpenBillingPeriodCommand(draft.Id.Value), default);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.SettlementOrder);
        draft.State.Should().Be(BillingState.Draft);
    }

    [Fact]
    public async Task CloseBillingPeriod_BumpsBothVersions_AndStampsTrackedCups()
    {
        var world = LedgerWorld();
        var january = world.AddPeriod("January", Jan1, Jan31, BillingState.Open);
        var cup = world.LegacyCup(world.Admin, 10m, Jan1.AddDays(3));
        var versionBefore = january.Version;
        var ledgerVersionBefore = world.Space.LedgerVersion;

        var result = await new CloseBillingPeriodHandler(world.BillingPeriodRepository, world.SpaceRepository, world.ConsumptionRepository, world.UserContext, world.Clock)
            .Handle(new CloseBillingPeriodCommand(january.Id.Value), default);

        result.IsSuccess.Should().BeTrue();
        cup.BillingPeriodId.Should().Be(january.Id);
        january.Version.Should().BeGreaterThan(versionBefore);
        world.Space.LedgerVersion.Should().BeGreaterThan(ledgerVersionBefore);
        await world.ConsumptionRepository.Received().GetBySpecForUpdateAsync(Arg.Any<ISpec<ConsumptionEntry>>(), Arg.Any<CancellationToken>());
    }
    // Closing an open-ended period ends it today; one that starts later is refused, not failed with an exception.
    [Fact]
    public async Task ClosingAnOpenEndedPeriod_BeforeItStarts_IsRefused()
    {
        var world = new LedgerWorld("CZK");
        var future = BillingPeriod.Create(world.Space.Id, "Next", world.Clock.UtcNow.Date.AddDays(5), null, world.Admin, world.Clock);
        future.Open(world.Admin, world.Clock);
        world.Periods.Add(future);

        var result = await new CloseBillingPeriodHandler(world.BillingPeriodRepository, world.SpaceRepository, world.ConsumptionRepository,
                world.UserContext, world.Clock)
            .Handle(new CloseBillingPeriodCommand(future.Id.Value), CancellationToken.None);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.InvalidBillingPeriodState);
        future.State.Should().Be(BillingState.Open);
        future.IsOpenEnded.Should().BeTrue();
    }

    [Fact]
    public async Task ClosingAnOpenEndedPeriod_ThatStartsToday_Works()
    {
        var world = new LedgerWorld("CZK");
        var current = BillingPeriod.Create(world.Space.Id, "Now", world.Clock.UtcNow.Date, null, world.Admin, world.Clock);
        current.Open(world.Admin, world.Clock);
        world.Periods.Add(current);

        var result = await new CloseBillingPeriodHandler(world.BillingPeriodRepository, world.SpaceRepository, world.ConsumptionRepository,
                world.UserContext, world.Clock)
            .Handle(new CloseBillingPeriodCommand(current.Id.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        current.EndDate.Should().Be(world.Clock.UtcNow.Date.AddDays(1).AddTicks(-1));
    }
}
