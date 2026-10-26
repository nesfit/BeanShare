using BeanShare.Domain.Aggregates.Ledger;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Exceptions;
using BeanShare.Domain.ValueObjects;
using NSubstitute;

namespace BeanShare.Application.Tests.Features.Balances;

public sealed class BalanceEntryDomainTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LedgerStart = new(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc);

    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly SpaceId _spaceId = SpaceId.New();
    private readonly UserId _member = UserId.New();
    private readonly UserId _treasurer = UserId.New();

    public BalanceEntryDomainTests()
    {
        _clock.UtcNow.Returns(Now);
    }

    private BalanceEntry Create(
        decimal amount = -538m,
        DateTime? date = null,
        BalanceEntryKind kind = BalanceEntryKind.Opening,
        string note = "Excel cutoff",
        DateTime? ledgerStart = null,
        UserId? counterparty = null) =>
        BalanceEntry.Create(_spaceId, _member, counterparty ?? _treasurer, amount, Currency.CZK,
            date ?? LedgerStart, kind, note, _treasurer, ledgerStart ?? LedgerStart, _clock);

    // T-DOM-01
    [Fact]
    public void Create_WithValidData_StoresSignedAmountForMemberAndMirrorsCounterparty()
    {
        var entry = Create();

        entry.Amount.Should().Be(-538m);
        entry.Currency.Should().Be("CZK");
        entry.EffectiveDate.Should().Be(LedgerStart);
        entry.EffectiveDate.Kind.Should().Be(DateTimeKind.Utc);
        entry.AmountFor(_member).Should().Be(-538m);
        entry.AmountFor(_treasurer).Should().Be(538m);
        entry.AmountFor(UserId.New()).Should().Be(0m);
        entry.IsLocked.Should().BeFalse();
        entry.Version.Should().Be(0);
    }

    [Fact]
    public void Create_WithZeroAmount_Throws()
    {
        var act = () => Create(amount: 0m);
        act.Should().Throw<ArgumentException>().WithMessage("*zero*");
    }

    [Fact]
    public void Create_WithMoreDecimalsThanTheCurrency_Throws()
    {
        var act = () => Create(amount: 10.005m);
        act.Should().Throw<ArgumentException>().WithMessage("*decimal places*");
    }

    [Fact]
    public void Create_AboveTheLimit_Throws()
    {
        var act = () => Create(amount: -1_000_000.01m);
        act.Should().Throw<ArgumentException>().WithMessage("*at most*");
    }

    [Fact]
    public void Create_WithTheMemberAsCounterparty_Throws()
    {
        var act = () => Create(counterparty: _member);
        act.Should().Throw<ArgumentException>().WithMessage("*differ*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutNote_Throws(string note)
    {
        var act = () => Create(note: note);
        act.Should().Throw<ArgumentException>().WithMessage("*note*");
    }

    [Fact]
    public void Create_WithTooLongNote_Throws()
    {
        var act = () => Create(note: new string('x', BalanceEntry.MaxNoteLength + 1));
        act.Should().Throw<ArgumentException>().WithMessage("*500*");
    }

    [Fact]
    public void Create_OpeningAfterTheLedgerStart_Throws()
    {
        var act = () => Create(date: LedgerStart.AddDays(1));
        act.Should().Throw<ArgumentException>().WithMessage("*ledger start*");
    }

    [Fact]
    public void Create_OpeningWithoutLedgerStart_MayNotBeInTheFuture()
    {
        var noStart = () => BalanceEntry.Create(_spaceId, _member, _treasurer, 10m, Currency.CZK, Now.AddDays(1),
            BalanceEntryKind.Opening, "x", _treasurer, null, _clock);
        noStart.Should().Throw<ArgumentException>().WithMessage("*future*");

        var today = BalanceEntry.Create(_spaceId, _member, _treasurer, 10m, Currency.CZK, Now,
            BalanceEntryKind.Opening, "x", _treasurer, null, _clock);
        today.EffectiveDate.Should().Be(Now.Date);
    }

    [Fact]
    public void Create_CorrectionAfterTheLedgerStart_IsAllowedUpToToday()
    {
        var entry = Create(kind: BalanceEntryKind.Correction, date: Now);
        entry.EffectiveDate.Should().Be(Now.Date);

        var future = () => Create(kind: BalanceEntryKind.Correction, date: Now.AddDays(1));
        future.Should().Throw<ArgumentException>().WithMessage("*future*");
    }

    [Fact]
    public void Create_CzkAcceptsTwoDecimalsAndJpyNone()
    {
        Create(amount: 120.50m).Amount.Should().Be(120.50m);

        var yen = () => BalanceEntry.Create(_spaceId, _member, _treasurer, 1.5m, Currency.JPY, LedgerStart,
            BalanceEntryKind.Opening, "x", _treasurer, LedgerStart, _clock);
        yen.Should().Throw<ArgumentException>().WithMessage("*0 decimal places*");
    }

    // T-DOM-02
    [Fact]
    public void Update_WhileEditable_ChangesTheEntryAndBumpsVersion()
    {
        var entry = Create();
        var editor = UserId.New();

        entry.Update(_member, _treasurer, 120m, LedgerStart.AddDays(-1), BalanceEntryKind.Opening, "fixed", editor, LedgerStart, _clock);

        entry.Amount.Should().Be(120m);
        entry.Note.Should().Be("fixed");
        entry.UpdatedBy.Should().Be(editor);
        entry.UpdatedAt.Should().Be(Now);
        entry.Version.Should().Be(1);
    }

    [Fact]
    public void EnsureEditable_WhileNotIncluded_DoesNotThrow()
    {
        var act = () => Create().EnsureEditable();
        act.Should().NotThrow();
    }

    // T-DOM-03
    [Fact]
    public void MarkIncluded_LocksTheEntry_UpdateAndDeleteThrowEntryLocked()
    {
        var entry = Create();
        var settlementId = SettlementId.New();

        entry.MarkIncluded(settlementId);

        entry.IsLocked.Should().BeTrue();
        entry.IncludedInSettlementId.Should().Be(settlementId);
        entry.Version.Should().Be(1);

        var update = () => entry.Update(_member, _treasurer, 1m, LedgerStart, BalanceEntryKind.Opening, "x", _treasurer, LedgerStart, _clock);
        update.Should().Throw<LedgerDomainException>().Which.Code.Should().Be("ENTRY_LOCKED");

        var delete = () => entry.EnsureEditable();
        delete.Should().Throw<LedgerDomainException>().Which.Code.Should().Be("ENTRY_LOCKED");

        var includeAgain = () => entry.MarkIncluded(SettlementId.New());
        includeAgain.Should().Throw<LedgerDomainException>();
    }

    [Fact]
    public void LockedEntry_CanBeCorrectedWithANewEntry()
    {
        var entry = Create();
        entry.MarkIncluded(SettlementId.New());

        var correction = Create(amount: 50m, kind: BalanceEntryKind.Correction, date: Now, note: "refund");

        correction.IsLocked.Should().BeFalse();
        correction.Kind.Should().Be(BalanceEntryKind.Correction);
    }
}
