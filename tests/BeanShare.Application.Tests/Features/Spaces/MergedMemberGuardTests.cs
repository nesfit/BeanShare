using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Commands.DeleteBalanceEntry;
using BeanShare.Application.Features.Balances.Commands.UpdateBalanceEntry;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using MapsterMapper;
using NSubstitute;

namespace BeanShare.Application.Tests.Features.Spaces;

// A merged placeholder stays a hidden member, and nothing new can name it.
public sealed class MergedMemberGuardTests
{
    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly LedgerWorld _world = new("CZK", new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc));
    private readonly UserId _alice;
    private readonly UserId _placeholder;

    public MergedMemberGuardTests()
    {
        _alice = _world.AddMember("Alice");
        _placeholder = _world.AddPlaceholder("Old Pete", "pete@example.org");
        _world.Space.SetTreasurer(_world.Admin);
    }

    private RemoveMemberCommandHandler RemoveHandler() => new(
        _world.SpaceRepository, _world.SettlementRepository, _world.SnapshotService, _world.UserContext,
        Substitute.For<IMapper>(), _world.Clock, _world.UserRepository);

    private LeaveSpaceCommandHandler LeaveHandler() => new(
        _world.SpaceRepository, _world.SettlementRepository, _world.SnapshotService, _world.UserContext, _world.Clock, _world.UserRepository);

    [Fact]
    public async Task RemoveAndLeave_OfAMergedPlaceholder_FailWithMemberMerged()
    {
        (await _world.MergeAsync(_placeholder, _alice)).IsSuccess.Should().BeTrue();

        var removed = await RemoveHandler().Handle(new RemoveMemberCommand(_world.Space.Id.Value, _placeholder.Value), default);
        removed.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.MemberMerged);

        _world.Actor = _placeholder;
        var left = await LeaveHandler().Handle(new LeaveSpaceCommand(_world.Space.Id.Value), default);
        left.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.MemberMerged);

        _world.Space.HasMember(_placeholder).Should().BeTrue("it stays a hidden member for later sweeps");
    }

    [Fact]
    public async Task AnEntryNamingAMergedUser_CannotBeEdited_ButCanBeDeleted()
    {
        var entry = _world.AddEntry(_placeholder, -50m, Jan1, counterparty: _alice);
        (await _world.MergeAsync(_placeholder, _alice)).IsSuccess.Should().BeTrue();

        var updated = await new UpdateBalanceEntryHandler(
                _world.SpaceRepository, _world.BalanceEntryRepository, _world.UserService, _world.UserContext, _world.Clock, _world.UserRepository)
            .Handle(new UpdateBalanceEntryCommand(_world.Space.Id.Value, entry.Id.Value, _world.Admin.Value, _alice.Value, -40m, Jan1,
                "moved", BalanceEntryKind.Correction), default);
        updated.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.MemberMerged);
        entry.UserId.Should().Be(_placeholder);
        entry.Amount.Should().Be(-50m);

        var deleted = await new DeleteBalanceEntryHandler(_world.SpaceRepository, _world.BalanceEntryRepository)
            .Handle(new DeleteBalanceEntryCommand(_world.Space.Id.Value, entry.Id.Value), default);
        deleted.IsSuccess.Should().BeTrue();
        _world.Entries.Should().NotContain(entry);
    }

    [Fact]
    public async Task ATargetWhoseBalanceIsOnlySwept_CannotLeaveOrBeRemoved()
    {
        _world.AddEntry(_placeholder, -50m, Jan1);
        (await _world.MergeAsync(_placeholder, _alice)).IsSuccess.Should().BeTrue();
        (await _world.SnapshotService.RunningAsync(_world.Space, default)).Calculation.NetFor(_alice).Should().Be(-50m);

        var removed = await RemoveHandler().Handle(new RemoveMemberCommand(_world.Space.Id.Value, _alice.Value), default);
        removed.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.MemberHasBalance);

        _world.Actor = _alice;
        var left = await LeaveHandler().Handle(new LeaveSpaceCommand(_world.Space.Id.Value), default);
        left.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.MemberHasBalance);
        _world.Space.HasMember(_alice).Should().BeTrue();
    }

    [Fact]
    public async Task UpdatePlaceholderMember_OfAMergedPlaceholder_FailsAndChangesNothing()
    {
        (await _world.MergeAsync(_placeholder, _alice)).IsSuccess.Should().BeTrue();
        var user = _world.UserOf(_placeholder);
        var (name, email) = (user.Name, user.Email);

        var result = await new UpdatePlaceholderMemberCommandHandler(_world.SpaceRepository, _world.UserRepository, _world.UserService)
            .Handle(new UpdatePlaceholderMemberCommand(_world.Space.Id.Value, _placeholder.Value, "Revived", "pete@example.org"), default);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.MemberMerged);
        user.Name.Should().Be(name);
        user.Email.Should().Be(email);
    }
}
