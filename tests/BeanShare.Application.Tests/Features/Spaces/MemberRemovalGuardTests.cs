using BeanShare.Application.Common;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Application.Features.Spaces.Dtos;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using MapsterMapper;
using NSubstitute;
using SettlementAggregate = BeanShare.Domain.Aggregates.Settlement.Settlement;

namespace BeanShare.Application.Tests.Features.Spaces;

// T-MEM-01: who may leave or be removed under the running balance (§3.4, B13, DS-34).
public sealed class MemberRemovalGuardTests
{
    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Jan31 = new(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb1 = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb28 = new(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc);

    private readonly LedgerWorld _world = new("CZK", new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc));
    private readonly UserId _alice;

    public MemberRemovalGuardTests()
    {
        _alice = _world.AddMember("Alice");
        _world.Space.SetTreasurer(_world.Admin);
    }

    private Task<Result<MemberActionDto>> RemoveAsync(UserId member) =>
        new RemoveMemberCommandHandler(_world.SpaceRepository, _world.SettlementRepository, _world.SnapshotService,
            _world.UserContext, Substitute.For<IMapper>(), _world.Clock, TestUsers.ActiveRepository()).Handle(new RemoveMemberCommand(_world.Space.Id.Value, member.Value), default);

    private Task<Result<LeaveSpaceResult>> LeaveAsync(UserId member)
    {
        _world.Actor = member;
        return new LeaveSpaceCommandHandler(_world.SpaceRepository, _world.SettlementRepository, _world.SnapshotService,
            _world.UserContext, _world.Clock, TestUsers.ActiveRepository()).Handle(new LeaveSpaceCommand(_world.Space.Id.Value), default);
    }

    [Fact]
    public async Task ANonZeroRunningBalance_BlocksRemoveAndLeave()
    {
        _world.AddEntry(_alice, -10m, Jan1);

        (await RemoveAsync(_alice)).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.MemberHasBalance);
        (await LeaveAsync(_alice)).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.MemberHasBalance);
        _world.Space.HasMember(_alice).Should().BeTrue();
    }

    [Fact]
    public async Task AnUnconfirmedLegacyLine_BlocksRemoval()
    {
        var january = _world.AddPeriod("January", Jan1, Jan31, BillingState.Settled);
        var legacy = SettlementAggregate.Create(_world.Space.Id, january.Id, "CZK", _world.Admin, _world.Clock);
        legacy.AddLine(_alice, 10m, 5m, _world.Clock);
        legacy.FinalizeGeneration(_world.Clock);
        _world.Settlements.Add(legacy);

        (await RemoveAsync(_alice)).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.UnpaidSettlements);
    }

    [Fact]
    public async Task TheTreasurer_CannotBeRemoved()
    {
        var other = _world.AddMember("Other admin", admin: true);
        _world.Space.SetTreasurer(other);

        (await RemoveAsync(other)).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.MemberHasBalance);
    }

    [Fact]
    public async Task OnlyCarriedAndConfirmedLinesWithAZeroBalance_MayLeave()
    {
        _world.AddEntry(_alice, -10m, Jan1);
        await _world.GenerateAsync(_world.AddPeriod("January", Jan1, Jan31));
        await _world.GenerateAsync(_world.AddPeriod("February", Feb1, Feb28));
        (await _world.ConfirmAsync(_world.Settlements.OrderBy(s => s.Sequence).Last(), _alice)).IsSuccess.Should().BeTrue();

        var result = await LeaveAsync(_alice);

        result.IsSuccess.Should().BeTrue("the carried line was confirmed in the settlement it was carried into (B13)");
        _world.Space.HasMember(_alice).Should().BeFalse();
    }

    [Fact]
    public async Task ThePayerOfANonEmptyBag_CannotLeaveUntilThePayerIsReassigned()
    {
        var bag = _world.AddBag(1000m, 500m, Jan1, paidBy: _alice);

        (await LeaveAsync(_alice)).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.MemberHasBalance);

        _world.Stock.SetPurchasePaidBy(bag.Id, _world.Admin, _world.Clock);
        (await LeaveAsync(_alice)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveAndLeave_BumpTheLedgerVersion()
    {
        var bob = _world.AddMember("Bob");
        var before = _world.Space.LedgerVersion;

        (await RemoveAsync(bob)).IsSuccess.Should().BeTrue();
        _world.Space.LedgerVersion.Should().BeGreaterThan(before);

        var afterRemove = _world.Space.LedgerVersion;
        (await LeaveAsync(_alice)).IsSuccess.Should().BeTrue();
        _world.Space.LedgerVersion.Should().BeGreaterThan(afterRemove);
    }
}
