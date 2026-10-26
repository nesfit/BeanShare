using BeanShare.Application.Abstractions;
using BeanShare.Application.Features.Balances.Queries.GetLedgerStartImpact;
using BeanShare.Application.Features.Balances.Queries.GetSpaceBalances;
using BeanShare.Application.Services;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Aggregates.Ledger;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using NSubstitute;
using SettlementAggregate = BeanShare.Domain.Aggregates.Settlement.Settlement;

namespace BeanShare.Application.Tests.Features.Balances;

public sealed class BalanceQueryTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);

    private readonly ISpaceRepository _spaceRepository = Substitute.For<ISpaceRepository>();
    private readonly IBalanceEntryRepository _entryRepository = Substitute.For<IBalanceEntryRepository>();
    private readonly ISettlementRepository _settlementRepository = Substitute.For<ISettlementRepository>();
    private readonly IUserService _userService = Substitute.For<IUserService>();
    private readonly IUserContext _userContext = Substitute.For<IUserContext>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private readonly UserId _admin = UserId.New();
    private readonly UserId _member = UserId.New();
    private readonly Space _space;

    public BalanceQueryTests()
    {
        _clock.UtcNow.Returns(Now);
        _space = Space.Create(SpaceId.New(), "Kava", Currency.CZK, _admin, new InviteCode("KAVAXY"), _clock);
        _space.Join(_member, _clock);
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), Arg.Any<CancellationToken>()).Returns(_space);
        _userService.GetByIdsAsync(Arg.Any<IEnumerable<UserId>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<UserId>>().Select(id => User.CreateWithIdAndPassword(id.Value, $"{id}@example.org", id.ToString(), "hash", Now)).ToList());
        _settlementRepository.GetBySpaceIdAsync(_space.Id, Arg.Any<CancellationToken>()).Returns(new List<SettlementAggregate>());
    }

    private GetSpaceBalancesHandler CreateHandler() =>
        new(_spaceRepository, _entryRepository, _settlementRepository, EmptyLedger.SnapshotService(_clock), _userService, _userContext);

    [Fact]
    public async Task GetSpaceBalances_AsMember_AsksOnlyForOwnEntriesAndShowsThemFromOwnSide()
    {
        _userContext.CurrentUserId.Returns(_member);
        var entry = BalanceEntry.Create(_space.Id, _admin, _member, 40m, Currency.CZK, Now, BalanceEntryKind.Correction, "refund", _admin, null, _clock);
        _entryRepository.GetVisibleAsync(_space.Id, _member, Arg.Any<CancellationToken>()).Returns(new List<BalanceEntry> { entry });

        var result = await CreateHandler().Handle(new GetSpaceBalancesQuery(_space.Id.Value), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsAdmin.Should().BeFalse();
        result.Value.Entries.Should().ContainSingle().Which.ViewerAmount.Should().Be(-40m);
        await _entryRepository.DidNotReceive().GetVisibleAsync(_space.Id, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSpaceBalances_AsAdmin_SeesAllEntriesAndOpenLegacyLines()
    {
        _userContext.CurrentUserId.Returns(_admin);
        _entryRepository.GetVisibleAsync(_space.Id, null, Arg.Any<CancellationToken>()).Returns(new List<BalanceEntry>());
        var legacy = SettlementAggregate.Create(_space.Id, BillingPeriodId.New(), "CZK", _admin, _clock);
        legacy.AddLine(_member, 10m, 5m, _clock);
        legacy.AddLine(_admin, 10m, 0m, _clock);
        legacy.FinalizeGeneration(_clock);
        _settlementRepository.GetBySpaceIdAsync(_space.Id, Arg.Any<CancellationToken>()).Returns(new List<SettlementAggregate> { legacy });

        var result = await CreateHandler().Handle(new GetSpaceBalancesQuery(_space.Id.Value), default);

        result.Value.IsAdmin.Should().BeTrue();
        result.Value.OpenLegacyLineCount.Should().Be(1);
        result.Value.Settings.IsLedgerStartLocked.Should().BeFalse();
    }

    [Fact]
    public async Task GetLedgerStartImpact_CountsUnbilledCupsBeforeTheDate()
    {
        var consumptions = Substitute.For<IConsumptionRepository>();
        var periods = Substitute.For<IBillingPeriodRepository>();
        var settled = BillingPeriod.Create(_space.Id, "Old", Now.AddDays(-60), Now.AddDays(-40), _admin, _clock);
        settled.Open(_admin, _clock);
        settled.Close(_admin, _clock);
        settled.MarkAsSettled(_admin, _clock);
        periods.GetBySpaceIdAsync(_space.Id, Arg.Any<CancellationToken>()).Returns(new List<BillingPeriod> { settled });

        var billed = Cup(Now.AddDays(-50));
        billed.AssignToBillingPeriod(settled.Id);
        var cups = new List<ConsumptionEntry> { billed, Cup(Now.AddDays(-20)), Cup(Now.AddDays(-12)) };
        consumptions.GetBySpecAsync(Arg.Any<ISpec<ConsumptionEntry>>(), Arg.Any<CancellationToken>()).Returns(cups);

        var result = await new GetLedgerStartImpactHandler(consumptions, periods)
            .Handle(new GetLedgerStartImpactQuery(_space.Id.Value, Now.AddDays(-10)), default);

        result.Value.CupsBeforeStart.Should().Be(2);
        result.Value.LedgerStartDate.Should().Be(Now.AddDays(-10).Date);
    }

    private ConsumptionEntry Cup(DateTime at) =>
        ConsumptionEntry.Create(_space.Id, _member, CoffeeProduct.Create("Alfa", "R", CoffeeType.Filter), Weight.FromGrams(10), at, _clock);
}
