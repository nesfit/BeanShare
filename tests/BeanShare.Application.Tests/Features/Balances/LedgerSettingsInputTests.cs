using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;
using BeanShare.Application.Services;
using BeanShare.Domain.Aggregates.Ledger;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using NSubstitute;

namespace BeanShare.Application.Tests.Features.Balances;

// A settings change touches only what it names (code round 1, N2), and every ledger date is normalised
// the same way (CR-4).
public sealed class LedgerSettingsInputTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Start = new(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc);

    private readonly ISpaceRepository _spaceRepository = Substitute.For<ISpaceRepository>();
    private readonly ISettlementRepository _settlementRepository = Substitute.For<ISettlementRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly UserId _admin = UserId.New();
    private readonly UserId _secondAdmin = UserId.New();
    private readonly Space _space;

    public LedgerSettingsInputTests()
    {
        _clock.UtcNow.Returns(Now);
        _space = Space.Create(SpaceId.New(), "Kava", Currency.CZK, _admin, new InviteCode("KAVAXY"), _clock);
        _space.Join(_secondAdmin, _clock);
        _space.PromoteMember(_secondAdmin, _clock);
        _space.SetTreasurer(_admin);
        _space.SetLedgerStartDate(Start, hasLedgerSettlement: false);
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), Arg.Any<CancellationToken>()).Returns(_space);
    }

    private UpdateLedgerSettingsHandler CreateHandler() => new(_spaceRepository, _settlementRepository, Substitute.For<IUserService>(), TestUsers.ActiveRepository(), Substitute.For<IBillingPeriodRepository>());

    [Fact]
    public async Task ChangingOnlyTheTreasurer_KeepsTheLedgerStart()
    {
        var result = await CreateHandler().Handle(new UpdateLedgerSettingsCommand(_space.Id.Value, _secondAdmin.Value, null), default);

        result.IsSuccess.Should().BeTrue();
        _space.TreasurerUserId.Should().Be(_secondAdmin);
        _space.LedgerStartDate.Should().Be(Start);
    }

    [Fact]
    public async Task ChangingOnlyTheTreasurer_AfterALedgerSettlement_IsNotLocked()
    {
        _settlementRepository.HasLedgerSettlementAsync(_space.Id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().Handle(new UpdateLedgerSettingsCommand(_space.Id.Value, _secondAdmin.Value, null), default);

        result.IsSuccess.Should().BeTrue();
        _space.LedgerStartDate.Should().Be(Start);
    }

    [Fact]
    public async Task ChangingOnlyTheLedgerStart_KeepsTheTreasurer()
    {
        var result = await CreateHandler().Handle(new UpdateLedgerSettingsCommand(_space.Id.Value, null, Start.AddDays(1)), default);

        result.IsSuccess.Should().BeTrue();
        _space.TreasurerUserId.Should().Be(_admin);
        _space.LedgerStartDate.Should().Be(Start.AddDays(1));
    }

    [Fact]
    public async Task TheClearFlags_RemoveTheSettings()
    {
        var result = await CreateHandler().Handle(
            new UpdateLedgerSettingsCommand(_space.Id.Value, null, null, ClearTreasurer: true, ClearLedgerStartDate: true), default);

        result.IsSuccess.Should().BeTrue();
        _space.TreasurerUserId.Should().BeNull();
        _space.LedgerStartDate.Should().BeNull();
    }

    [Fact]
    public void SettingAndClearingTheSameValue_IsRefused()
    {
        var validator = new UpdateLedgerSettingsValidator(_clock);

        validator.Validate(new UpdateLedgerSettingsCommand(_space.Id.Value, _admin.Value, null, ClearTreasurer: true)).IsValid.Should().BeFalse();
        validator.Validate(new UpdateLedgerSettingsCommand(_space.Id.Value, null, Start, ClearLedgerStartDate: true)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ALocalKindDate_NormalisesToTheSameUtcDayForEntriesAndTheLedgerStart()
    {
        var local = new DateTime(2026, 5, 1, 0, 30, 0, DateTimeKind.Local);
        var expected = DateTime.SpecifyKind(local.ToUniversalTime().Date, DateTimeKind.Utc);
        var space = Space.Create(SpaceId.New(), "Kava", Currency.CZK, _admin, new InviteCode("KAVAXY"), _clock);

        space.SetLedgerStartDate(local, hasLedgerSettlement: false);

        space.LedgerStartDate.Should().Be(expected);
        space.LedgerStartDate!.Value.Kind.Should().Be(DateTimeKind.Utc);
        BalanceEntry.NormalizeDate(local).Should().Be(expected);
        UtcDate.Of(new DateTime(2026, 5, 1, 23, 59, 0, DateTimeKind.Unspecified)).Should().Be(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
    }
}
