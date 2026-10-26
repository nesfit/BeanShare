using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Commands.CommitBalanceEntriesPaste;
using BeanShare.Application.Features.Balances.Queries.PreviewBalanceEntriesPaste;
using BeanShare.Application.Features.Balances.Services;
using BeanShare.Application.Services;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Aggregates.Ledger;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using NSubstitute;

namespace BeanShare.Application.Tests.Features.Balances;

// T-DOM-05 (C1 part): the member;amount;note grammar, preview and all-or-nothing commit.
public sealed class BalancePasteTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LedgerStart = new(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc);

    private readonly ISpaceRepository _spaceRepository = Substitute.For<ISpaceRepository>();
    private readonly IBalanceEntryRepository _entryRepository = Substitute.For<IBalanceEntryRepository>();
    private readonly IUserService _userService = Substitute.For<IUserService>();
    private readonly IUserContext _userContext = Substitute.For<IUserContext>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private readonly User _honza = User.CreateWithIdAndPassword(Guid.NewGuid(), "h@example.org", "Honza", "hash", Now);
    private readonly User _alice = User.CreateWithIdAndPassword(Guid.NewGuid(), "a@example.org", "Alice Novak", "hash", Now);
    private readonly User _bob = User.CreateWithIdAndPassword(Guid.NewGuid(), "b@example.org", "Bob", "hash", Now);
    private readonly Space _space;

    public BalancePasteTests()
    {
        _clock.UtcNow.Returns(Now);
        _userContext.CurrentUserId.Returns(_honza.Id);
        _space = Space.Create(SpaceId.New(), "Kava", Currency.CZK, _honza.Id, new InviteCode("KAVAXY"), _clock);
        _space.Join(_alice.Id, _clock);
        _space.Join(_bob.Id, _clock);
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), Arg.Any<CancellationToken>()).Returns(_space);
        var users = new[] { _honza, _alice, _bob };
        _userService.GetByIdsAsync(Arg.Any<IEnumerable<UserId>>(), Arg.Any<CancellationToken>())
            .Returns(ci => users.Where(u => ci.Arg<IEnumerable<UserId>>().Contains(u.Id)).ToList());
    }

    private PreviewBalanceEntriesPasteHandler CreatePreviewHandler() =>
        new(_spaceRepository, EmptyLedger.SnapshotService(_clock), _userService, _userContext, _clock);

    private CommitBalanceEntriesPasteHandler CreateCommitHandler() =>
        new(_spaceRepository, _entryRepository, _userService, _userContext, _clock);

    [Theory]
    [InlineData("120,00", 120.00)]
    [InlineData("120.5", 120.5)]
    [InlineData("-538", -538)]
    [InlineData("+7,25", 7.25)]
    [InlineData(" -0.01 ", -0.01)]
    public void TryParseAmount_AcceptsDotOrCommaDecimals(string text, double expected)
    {
        BalancePasteEvaluator.TryParseAmount(text, out var amount).Should().BeTrue();
        amount.Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData("1,234.56")]
    [InlineData("1 234")]
    [InlineData("12e3")]
    [InlineData("abc")]
    [InlineData("")]
    public void TryParseAmount_RejectsThousandsSeparatorsAndJunk(string text)
    {
        BalancePasteEvaluator.TryParseAmount(text, out _).Should().BeFalse();
    }

    [Fact]
    public async Task Preview_MatchesEmailOrNameSkipsHeaderAndBlankLinesAndSums()
    {
        var text = "member;amount;note\n\na@example.org;-538,00;from sheet\r\nBOB;120.00\n";

        var result = await CreatePreviewHandler().Handle(
            new PreviewBalanceEntriesPasteQuery(_space.Id.Value, text, BatchNote: "Excel cutoff"), default);

        result.IsSuccess.Should().BeTrue();
        var preview = result.Value;
        preview.Rows.Should().HaveCount(2);
        preview.Rows[0].UserId.Should().Be(_alice.Id.Value);
        preview.Rows[0].LineNumber.Should().Be(3);
        preview.Rows[0].Note.Should().Be("from sheet");
        preview.Rows[1].UserId.Should().Be(_bob.Id.Value);
        preview.Rows[1].Note.Should().Be("Excel cutoff");
        preview.Total.Should().Be(-418m);
        preview.ErrorRowCount.Should().Be(0);
        preview.CanCommit.Should().BeTrue();
        preview.CounterpartyUserId.Should().Be(_honza.Id.Value);
        preview.Kind.Should().Be("Opening");
    }

    [Fact]
    public async Task Preview_DefaultsTheDateToTheLedgerStartElseToday()
    {
        var today = await CreatePreviewHandler().Handle(
            new PreviewBalanceEntriesPasteQuery(_space.Id.Value, "Bob;1", BatchNote: "x"), default);
        today.Value.EffectiveDate.Should().Be(Now.Date);

        _space.SetLedgerStartDate(LedgerStart, hasLedgerSettlement: false);
        var atStart = await CreatePreviewHandler().Handle(
            new PreviewBalanceEntriesPasteQuery(_space.Id.Value, "Bob;1", BatchNote: "x"), default);
        atStart.Value.EffectiveDate.Should().Be(LedgerStart);
    }

    [Fact]
    public async Task Preview_ReportsErrorsPerRow()
    {
        var text = string.Join('\n',
            "nobody@example.org;10;x",
            "Bob;ten;x",
            "Bob;0;x",
            "Alice Novak;5",
            "Honza;5;x",
            "Bob;1;x;extra");

        var result = await CreatePreviewHandler().Handle(new PreviewBalanceEntriesPasteQuery(_space.Id.Value, text), default);

        var rows = result.Value.Rows;
        rows.Should().HaveCount(6);
        rows[0].Errors.Should().ContainSingle(e => e.Contains("not a member"));
        rows[1].Errors.Should().ContainSingle(e => e.Contains("not an amount"));
        rows[2].Errors.Should().ContainSingle(e => e.Contains("zero"));
        rows[3].Errors.Should().ContainSingle(e => e.Contains("note"));
        rows[4].Errors.Should().ContainSingle(e => e.Contains("differ"));
        rows[5].Errors.Should().Contain(e => e.Contains("member;amount;note"));
        result.Value.ErrorRowCount.Should().Be(6);
        result.Value.Total.Should().Be(0m);
        result.Value.CanCommit.Should().BeFalse();
    }

    [Fact]
    public async Task Preview_WarnsAboutRepeatedMembers()
    {
        var result = await CreatePreviewHandler().Handle(
            new PreviewBalanceEntriesPasteQuery(_space.Id.Value, "Bob;1\nb@example.org;2", BatchNote: "x"), default);

        result.Value.Rows[1].Warnings.Should().ContainSingle();
        result.Value.Rows[1].IsValid.Should().BeTrue();
        result.Value.Total.Should().Be(3m);
    }

    [Fact]
    public async Task Preview_WithTwoMembersOfTheSameName_AsksForTheEmail()
    {
        var twin = User.CreateWithIdAndPassword(Guid.NewGuid(), "b2@example.org", "Bob", "hash", Now);
        _space.Join(twin.Id, _clock);
        var users = new[] { _honza, _alice, _bob, twin };
        _userService.GetByIdsAsync(Arg.Any<IEnumerable<UserId>>(), Arg.Any<CancellationToken>())
            .Returns(ci => users.Where(u => ci.Arg<IEnumerable<UserId>>().Contains(u.Id)).ToList());

        var result = await CreatePreviewHandler().Handle(
            new PreviewBalanceEntriesPasteQuery(_space.Id.Value, "Bob;1", BatchNote: "x"), default);

        result.Value.Rows[0].Errors.Should().ContainSingle(e => e.Contains("email"));
    }

    [Fact]
    public async Task Preview_WithACounterpartyOutsideTheSpace_IsABatchError()
    {
        var result = await CreatePreviewHandler().Handle(
            new PreviewBalanceEntriesPasteQuery(_space.Id.Value, "Bob;1", BatchNote: "x", CounterpartyUserId: Guid.NewGuid()), default);

        result.Value.Errors.Should().ContainSingle(e => e.Contains("counterparty"));
        result.Value.CanCommit.Should().BeFalse();
    }

    [Fact]
    public async Task Commit_WithAnyInvalidRow_SavesNothing()
    {
        var versionBefore = _space.LedgerVersion;

        var result = await CreateCommitHandler().Handle(
            new CommitBalanceEntriesPasteCommand(_space.Id.Value, "Bob;1\nnobody;2", BatchNote: "x"), default);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("Line 2"));
        await _entryRepository.DidNotReceive().AddAsync(Arg.Any<BalanceEntry>(), Arg.Any<CancellationToken>());
        _space.LedgerVersion.Should().Be(versionBefore);
    }

    [Fact]
    public async Task Commit_WithValidRows_CreatesOneEntryPerRowAgainstTheCounterparty()
    {
        var added = new List<BalanceEntry>();
        await _entryRepository.AddAsync(Arg.Do<BalanceEntry>(added.Add), Arg.Any<CancellationToken>());
        _space.SetLedgerStartDate(LedgerStart, hasLedgerSettlement: false);
        var versionBefore = _space.LedgerVersion;

        var result = await CreateCommitHandler().Handle(
            new CommitBalanceEntriesPasteCommand(_space.Id.Value, "a@example.org;-538,00\nBob;120,00;own note", BatchNote: "Excel cutoff"), default);

        result.IsSuccess.Should().BeTrue();
        added.Should().HaveCount(2);
        added.Should().OnlyContain(e => e.CounterpartyUserId == _honza.Id && e.EffectiveDate == LedgerStart && e.Kind == BalanceEntryKind.Opening);
        added[0].UserId.Should().Be(_alice.Id);
        added[0].Amount.Should().Be(-538m);
        added[0].Note.Should().Be("Excel cutoff");
        added[1].Note.Should().Be("own note");
        added.Sum(e => e.Amount).Should().Be(-418m);
        _space.LedgerVersion.Should().Be(versionBefore + 1);
        result.Value.Should().HaveCount(2);
        result.Value[0].ViewerAmount.Should().Be(538m, "the treasurer sees the mirrored amount");
    }
}
