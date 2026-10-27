using BeanShare.Application.Abstractions;
using BeanShare.Application.Features.Consumption.Queries;
using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;

namespace BeanShare.Application.Tests.Features.Consumption;

/// <summary>
/// The recent-cups list offers Edit and Delete separately: a write-off share can only be deleted (the edit command
/// refuses it), and nothing in a Closed or Settled period can be changed.
/// </summary>
public sealed class RecentConsumptionsActionTests
{
    private readonly IClock _clock = TestClock.Instance;
    private readonly SpaceId _spaceId = SpaceId.New();
    private readonly UserId _admin = new(Guid.NewGuid());
    private readonly CoffeeProduct _product = CoffeeProduct.Create("Keňa Duma", "Coffeespot", CoffeeType.Filter);

    [Fact]
    public async Task AWriteOffShare_CanBeDeletedButNotEdited()
    {
        var open = BillingPeriod.Create(_spaceId, "Open", _clock.UtcNow.AddDays(-30), null, _admin, _clock);
        open.Open(_admin, _clock);
        var closed = BillingPeriod.Create(_spaceId, "Closed", _clock.UtcNow.AddDays(-60), _clock.UtcNow.AddDays(-31), _admin, _clock);
        closed.Open(_admin, _clock);
        closed.Close(_admin, _clock);

        var cup = Cup(10, _clock.UtcNow.AddDays(-3), open.Id);
        var share = Cup(40, _clock.UtcNow.AddDays(-2), open.Id, writeOff: true);
        var unstampedShare = Cup(40, _clock.UtcNow.AddDays(-1), period: null, writeOff: true);
        var frozenCup = Cup(10, _clock.UtcNow.AddDays(-40), closed.Id);
        var frozenShare = Cup(40, _clock.UtcNow.AddDays(-39), closed.Id, writeOff: true);

        var result = await CreateHandler([cup, share, unstampedShare, frozenCup, frozenShare], [open, closed])
            .Handle(new GetRecentConsumptionsQuery(_spaceId.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Entries.ToDictionary(e => e.Id, e => (e.CanEdit, e.CanDelete)).Should().Equal(
            new Dictionary<Guid, (bool, bool)>
            {
                [cup.Id.Value] = (true, true),
                [share.Id.Value] = (false, true),
                [unstampedShare.Id.Value] = (false, true),
                [frozenCup.Id.Value] = (false, false),
                [frozenShare.Id.Value] = (false, false),
            });
    }

    [Fact]
    public async Task CupsBeforeTheLedgerStart_CanBeNeitherEditedNorDeleted()
    {
        var before = Cup(10, _clock.UtcNow.AddDays(-20), period: null);
        var after = Cup(11, _clock.UtcNow.AddDays(-5), period: null);

        var result = await CreateHandler([before, after], [], ledgerStart: _clock.UtcNow.AddDays(-10))
            .Handle(new GetRecentConsumptionsQuery(_spaceId.Value), CancellationToken.None);

        result.Value.Entries.ToDictionary(e => e.Id, e => (e.CanEdit, e.CanDelete)).Should().Equal(
            new Dictionary<Guid, (bool, bool)>
            {
                [before.Id.Value] = (false, false),
                [after.Id.Value] = (true, true),
            });
    }

    private ConsumptionEntry Cup(int grams, DateTime at, BillingPeriodId? period, bool writeOff = false)
    {
        var cup = ConsumptionEntry.Create(
            _spaceId, _admin, CoffeeProduct.Create(_product.Name, _product.Brand, _product.Type),
            Weight.FromGrams(grams), at, _clock, isWriteOff: writeOff);
        if (period is { } stamped)
            cup.AssignToBillingPeriod(stamped);
        return cup;
    }

    private GetRecentConsumptionsQueryHandler CreateHandler(
        List<ConsumptionEntry> cups, List<BillingPeriod> periods, DateTime? ledgerStart = null)
    {
        var space = Domain.Aggregates.Space.Space.Create(_spaceId, "Lab", Currency.USD, _admin, new InviteCode("CAFE23"), _clock);
        if (ledgerStart.HasValue)
            space.SetLedgerStartDate(ledgerStart, hasLedgerSettlement: false);
        var spaceRepo = Substitute.For<ISpaceRepository>();
        spaceRepo.GetSingleBySpecAsync(Arg.Any<Domain.Specifications.SpaceByIdSpecification>(), Arg.Any<CancellationToken>()).Returns(space);

        var consumptionRepo = Substitute.For<IConsumptionRepository>();
        consumptionRepo.GetRecentBySpaceIdAsync(Arg.Any<SpaceId>(), Arg.Any<int>(), Arg.Any<UserId?>(), Arg.Any<CancellationToken>())
            .Returns(cups);
        consumptionRepo.GetBySpaceIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>()).Returns(cups);

        var periodRepo = Substitute.For<IBillingPeriodRepository>();
        periodRepo.GetBySpaceIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>()).Returns(periods);

        var stockRepo = Substitute.For<ICoffeeStockRepository>();
        stockRepo.GetBySpaceIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>()).Returns((CoffeeStock?)null);

        var userRepo = Substitute.For<IUserRepository>();
        userRepo.GetByIdAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var userContext = Substitute.For<IUserContext>();
        userContext.CurrentUserId.Returns(_admin);

        return new GetRecentConsumptionsQueryHandler(consumptionRepo, stockRepo, spaceRepo, periodRepo, userRepo, userContext, _clock);
    }
}
