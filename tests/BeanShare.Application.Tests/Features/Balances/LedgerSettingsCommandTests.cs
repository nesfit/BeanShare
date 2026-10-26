using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Commands.CreateBalanceEntry;
using BeanShare.Application.Features.Balances.Commands.DeleteBalanceEntry;
using BeanShare.Application.Features.Balances.Commands.UpdateBalanceEntry;
using BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Application.Services;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Aggregates.Ledger;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MapsterMapper;
using NSubstitute;

namespace BeanShare.Application.Tests.Features.Balances;

// T-DOM-04 (C1 part): treasurer and ledger-start rules, and the LedgerVersion bumps (DS-28).
public sealed class LedgerSettingsCommandTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);

    private readonly ISpaceRepository _spaceRepository = Substitute.For<ISpaceRepository>();
    private readonly ISettlementRepository _settlementRepository = Substitute.For<ISettlementRepository>();
    private readonly IBalanceEntryRepository _entryRepository = Substitute.For<IBalanceEntryRepository>();
    private readonly IUserService _userService = Substitute.For<IUserService>();
    private readonly IUserContext _userContext = Substitute.For<IUserContext>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private readonly UserId _admin = UserId.New();
    private readonly UserId _secondAdmin = UserId.New();
    private readonly UserId _member = UserId.New();
    private readonly Space _space;

    public LedgerSettingsCommandTests()
    {
        _clock.UtcNow.Returns(Now);
        _userContext.CurrentUserId.Returns(_admin);
        _space = Space.Create(SpaceId.New(), "Kava", Currency.CZK, _admin, new InviteCode("KAVAXY"), _clock);
        _space.Join(_secondAdmin, _clock);
        _space.PromoteMember(_secondAdmin, _clock);
        _space.Join(_member, _clock);
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), Arg.Any<CancellationToken>()).Returns(_space);
        _userService.GetByIdAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(ci => User.CreateWithIdAndPassword(ci.Arg<UserId>().Value, "u@example.org", "User", "hash", Now));
        _userService.GetByIdsAsync(Arg.Any<IEnumerable<UserId>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<UserId>>().Select(id => User.CreateWithIdAndPassword(id.Value, $"{id}@example.org", id.ToString(), "hash", Now)).ToList());
    }

    private UpdateLedgerSettingsHandler CreateHandler() => new(_spaceRepository, _settlementRepository, _userService, TestUsers.ActiveRepository(), Substitute.For<IBillingPeriodRepository>());

    [Fact]
    public async Task SetTreasurer_ToAnAdmin_SucceedsAndBumpsLedgerVersion()
    {
        var before = _space.LedgerVersion;

        var result = await CreateHandler().Handle(new UpdateLedgerSettingsCommand(_space.Id.Value, _admin.Value, null), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.TreasurerUserId.Should().Be(_admin.Value);
        _space.TreasurerUserId.Should().Be(_admin);
        _space.LedgerVersion.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task SetTreasurer_ToANonMember_Fails()
    {
        var result = await CreateHandler().Handle(new UpdateLedgerSettingsCommand(_space.Id.Value, Guid.NewGuid(), null), default);

        result.IsFailure.Should().BeTrue();
        result.Errors[0].Code.Should().Be(Error.Codes.MemberNotFound);
        _space.TreasurerUserId.Should().BeNull();
    }

    [Fact]
    public async Task SetTreasurer_ToANonAdminMember_Fails()
    {
        var result = await CreateHandler().Handle(new UpdateLedgerSettingsCommand(_space.Id.Value, _member.Value, null), default);

        result.IsFailure.Should().BeTrue();
        result.Errors[0].Code.Should().Be(Error.Codes.ValidationError);
        _space.TreasurerUserId.Should().BeNull();
    }

    [Fact]
    public void SetTreasurer_DomainRefusesANonAdmin()
    {
        var act = () => _space.SetTreasurer(_member);
        act.Should().Throw<BeanShare.Domain.Exceptions.SpaceDomainException>();
    }

    [Fact]
    public async Task SetLedgerStart_BumpsLedgerVersionAndNormalizesToUtcDate()
    {
        var before = _space.LedgerVersion;
        var start = new DateTime(2026, 8, 17, 15, 30, 0, DateTimeKind.Utc);

        var result = await CreateHandler().Handle(new UpdateLedgerSettingsCommand(_space.Id.Value, null, start), default);

        result.IsSuccess.Should().BeTrue();
        _space.LedgerStartDate.Should().Be(new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc));
        _space.LedgerVersion.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task SetLedgerStart_AfterALedgerSettlement_IsLocked()
    {
        _space.SetLedgerStartDate(new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc), hasLedgerSettlement: false);
        _settlementRepository.HasLedgerSettlementAsync(_space.Id, Arg.Any<CancellationToken>()).Returns(true);

        var changed = await CreateHandler().Handle(
            new UpdateLedgerSettingsCommand(_space.Id.Value, null, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)), default);
        var unchanged = await CreateHandler().Handle(
            new UpdateLedgerSettingsCommand(_space.Id.Value, _admin.Value, new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc)), default);

        changed.IsFailure.Should().BeTrue();
        changed.Errors[0].Code.Should().Be(Error.Codes.LedgerStartLocked);
        unchanged.IsSuccess.Should().BeTrue();
        _space.LedgerStartDate.Should().Be(new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void SetLedgerStart_InTheFuture_IsRefusedByTheValidator()
    {
        var validator = new UpdateLedgerSettingsValidator(_clock);

        validator.Validate(new UpdateLedgerSettingsCommand(_space.Id.Value, null, Now.AddDays(1))).IsValid.Should().BeFalse();
        validator.Validate(new UpdateLedgerSettingsCommand(_space.Id.Value, null, Now)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task DemoteMember_OfTheTreasurer_IsRefused()
    {
        _space.SetTreasurer(_secondAdmin);
        var handler = new DemoteMemberCommandHandler(_spaceRepository, _userContext, _clock);

        var result = await handler.Handle(new DemoteMemberCommand(_space.Id.Value, _secondAdmin.Value), default);

        result.IsFailure.Should().BeTrue();
        result.Errors[0].Code.Should().Be(Error.Codes.CannotDemoteMember);
        _space.IsAdmin(_secondAdmin).Should().BeTrue();
    }

    [Fact]
    public async Task DemoteMember_BumpsLedgerVersion()
    {
        var before = _space.LedgerVersion;
        var handler = new DemoteMemberCommandHandler(_spaceRepository, _userContext, _clock);

        var result = await handler.Handle(new DemoteMemberCommand(_space.Id.Value, _secondAdmin.Value), default);

        result.IsSuccess.Should().BeTrue();
        _space.LedgerVersion.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task RemoveMember_OfTheTreasurer_IsRefused()
    {
        _space.SetTreasurer(_secondAdmin);
        _settlementRepository.GetBySpaceIdAsync(_space.Id, Arg.Any<CancellationToken>()).Returns(new List<BeanShare.Domain.Aggregates.Settlement.Settlement>());
        var handler = new RemoveMemberCommandHandler(_spaceRepository, _settlementRepository, EmptyLedger.SnapshotService(_clock), _userContext, Substitute.For<IMapper>(), _clock, TestUsers.ActiveRepository());

        var result = await handler.Handle(new RemoveMemberCommand(_space.Id.Value, _secondAdmin.Value), default);

        result.IsFailure.Should().BeTrue();
        _space.HasMember(_secondAdmin).Should().BeTrue();
    }

    [Fact]
    public async Task RemoveMember_BumpsLedgerVersion()
    {
        var before = _space.LedgerVersion;
        _settlementRepository.GetBySpaceIdAsync(_space.Id, Arg.Any<CancellationToken>()).Returns(new List<BeanShare.Domain.Aggregates.Settlement.Settlement>());
        var handler = new RemoveMemberCommandHandler(_spaceRepository, _settlementRepository, EmptyLedger.SnapshotService(_clock), _userContext, Substitute.For<IMapper>(), _clock, TestUsers.ActiveRepository());

        var result = await handler.Handle(new RemoveMemberCommand(_space.Id.Value, _member.Value), default);

        result.IsSuccess.Should().BeTrue();
        _space.LedgerVersion.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task LeaveSpace_BumpsLedgerVersion()
    {
        var before = _space.LedgerVersion;
        _userContext.CurrentUserId.Returns(_member);
        _settlementRepository.GetBySpaceIdAsync(_space.Id, Arg.Any<CancellationToken>()).Returns(new List<BeanShare.Domain.Aggregates.Settlement.Settlement>());
        var handler = new LeaveSpaceCommandHandler(_spaceRepository, _settlementRepository, EmptyLedger.SnapshotService(_clock), _userContext, _clock, TestUsers.ActiveRepository());

        var result = await handler.Handle(new LeaveSpaceCommand(_space.Id.Value), default);

        result.IsSuccess.Should().BeTrue();
        _space.LedgerVersion.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task EntryCreateUpdateAndDelete_EachBumpLedgerVersion()
    {
        BalanceEntry? stored = null;
        await _entryRepository.AddAsync(Arg.Do<BalanceEntry>(e => stored = e), Arg.Any<CancellationToken>());

        var v0 = _space.LedgerVersion;
        var created = await new CreateBalanceEntryHandler(_spaceRepository, _entryRepository, _userService, _userContext, _clock, TestUsers.ActiveRepository())
            .Handle(new CreateBalanceEntryCommand(_space.Id.Value, _member.Value, -538m, Now, "Excel cutoff"), default);
        created.IsSuccess.Should().BeTrue();
        _space.LedgerVersion.Should().BeGreaterThan(v0);

        _entryRepository.GetByIdAsync(stored!.Id, Arg.Any<CancellationToken>()).Returns(stored);

        var v1 = _space.LedgerVersion;
        var updated = await new UpdateBalanceEntryHandler(_spaceRepository, _entryRepository, _userService, _userContext, _clock, TestUsers.ActiveRepository())
            .Handle(new UpdateBalanceEntryCommand(_space.Id.Value, stored.Id.Value, _member.Value, _admin.Value, -500m, Now, "fixed", BalanceEntryKind.Opening), default);
        updated.IsSuccess.Should().BeTrue();
        _space.LedgerVersion.Should().BeGreaterThan(v1);

        var v2 = _space.LedgerVersion;
        var deleted = await new DeleteBalanceEntryHandler(_spaceRepository, _entryRepository)
            .Handle(new DeleteBalanceEntryCommand(_space.Id.Value, stored.Id.Value), default);
        deleted.IsSuccess.Should().BeTrue();
        _space.LedgerVersion.Should().BeGreaterThan(v2);
        _entryRepository.Received(1).Remove(stored);
    }

    [Fact]
    public async Task UpdateAndDelete_OfALockedEntry_FailWithEntryLocked()
    {
        var entry = BalanceEntry.Create(_space.Id, _member, _admin, -538m, Currency.CZK, Now, BalanceEntryKind.Opening,
            "Excel cutoff", _admin, null, _clock);
        entry.MarkIncluded(SettlementId.New());
        _entryRepository.GetByIdAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(entry);

        var updated = await new UpdateBalanceEntryHandler(_spaceRepository, _entryRepository, _userService, _userContext, _clock, TestUsers.ActiveRepository())
            .Handle(new UpdateBalanceEntryCommand(_space.Id.Value, entry.Id.Value, _member.Value, _admin.Value, -1m, Now, "x", BalanceEntryKind.Opening), default);
        var deleted = await new DeleteBalanceEntryHandler(_spaceRepository, _entryRepository)
            .Handle(new DeleteBalanceEntryCommand(_space.Id.Value, entry.Id.Value), default);

        updated.Errors[0].Code.Should().Be(Error.Codes.EntryLocked);
        deleted.Errors[0].Code.Should().Be(Error.Codes.EntryLocked);
        _entryRepository.DidNotReceive().Remove(Arg.Any<BalanceEntry>());
    }

    [Fact]
    public async Task CreateEntry_ForANonMember_Fails()
    {
        var result = await new CreateBalanceEntryHandler(_spaceRepository, _entryRepository, _userService, _userContext, _clock, TestUsers.ActiveRepository())
            .Handle(new CreateBalanceEntryCommand(_space.Id.Value, Guid.NewGuid(), -538m, Now, "Excel cutoff"), default);

        result.IsFailure.Should().BeTrue();
        result.Errors[0].Code.Should().Be(Error.Codes.MemberNotFound);
        await _entryRepository.DidNotReceive().AddAsync(Arg.Any<BalanceEntry>(), Arg.Any<CancellationToken>());
    }
}
