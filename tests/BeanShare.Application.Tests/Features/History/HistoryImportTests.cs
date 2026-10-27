using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;
using BeanShare.Application.Features.History.Commands.CommitHistoryImport;
using BeanShare.Application.Features.History.Commands.DeleteHistoryImport;
using BeanShare.Application.Features.History.Dtos;
using BeanShare.Application.Features.History.Queries.GetSpaceHistory;
using BeanShare.Application.Features.History.Queries.PreviewHistoryImport;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;

namespace BeanShare.Application.Tests.Features.History;

// The Excel history import: preview, commit, delete, and how history stays out of the money ledger.
public sealed class HistoryImportTests
{
    private static readonly DateTime LedgerStart = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private const string Cups = """
        period;start;end;member;cups
        2025 H1 room A;2025-01-01;2025-06-30;Alice;120
        2025 H1 room A;2025-01-01;2025-06-30;Admin;80
        2025 H1 room B;2025-03-01;2025-06-30;alice@example.org;5
        2025-12-31;2025-12-31;2025-12-31;Old Pete;3
        """;

    private const string Purchases = """
        period;date;paidBy;description;grams;cost
        2025 H1 room A;2025-02-03;Admin;Beans 1 kg;1000;450,50
        2025 H1 room A;;;Milk and sugar;;120
        2025 H1 room B;2025-04-01;Old Pete;Beans;500;250.00
        """;

    private readonly LedgerWorld _world = new("CZK", new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc));
    private readonly UserId _alice;

    public HistoryImportTests()
    {
        _alice = _world.AddMember("Alice");
        _world.Space.SetTreasurer(_world.Admin);
        _world.Space.SetLedgerStartDate(LedgerStart, hasLedgerSettlement: false);
    }

    private Task<Result<HistoryImportPreviewDto>> PreviewAsync(string cups, string? purchases = null, bool create = false) =>
        new PreviewHistoryImportHandler(_world.SpaceRepository, _world.UserService, _world.UserRepository)
            .Handle(new PreviewHistoryImportQuery(_world.Space.Id.Value, cups, purchases, create), default);

    private Task<Result<HistoryImportResultDto>> CommitAsync(string cups, string? purchases = null, bool create = false, string? note = null) =>
        new CommitHistoryImportHandler(_world.SpaceRepository, _world.BillingPeriodRepository, _world.HistoryImportRepository,
                _world.UserService, _world.UserRepository, _world.UserContext, _world.Clock)
            .Handle(new CommitHistoryImportCommand(_world.Space.Id.Value, cups, purchases, note, create), default);

    private Task<Result> DeleteAsync(Guid importId) =>
        new DeleteHistoryImportHandler(_world.SpaceRepository, _world.HistoryImportRepository, _world.BillingPeriodRepository)
            .Handle(new DeleteHistoryImportCommand(_world.Space.Id.Value, importId), default);

    [Fact]
    public async Task Preview_ParsesBothGrammars_WithUndatedPurchasesOverlappingRoomsAndASingleDayPeriod()
    {
        var preview = await PreviewAsync(Cups, Purchases, create: true);

        preview.IsSuccess.Should().BeTrue();
        var dto = preview.Value;
        dto.CanCommit.Should().BeTrue();
        dto.Periods.Select(p => p.Label).Should().Equal("2025 H1 room A", "2025 H1 room B", "2025-12-31");
        dto.Periods[0].Should().Match<HistoryPreviewPeriodDto>(p => p.Cups == 200 && p.Cost == 570.50m && p.PurchaseCount == 2);
        dto.Periods[2].StartDate.Should().Be(dto.Periods[2].EndDate, "a single-day period is allowed");
        dto.TotalCups.Should().Be(208);
        dto.TotalCost.Should().Be(820.50m);
        dto.PlaceholdersToCreate.Should().Equal("Old Pete");
        dto.Members.Single(m => m.Member == "Alice").Cups.Should().Be(125);
        dto.Members.Single(m => m.Member == "Old Pete").IsNewPlaceholder.Should().BeTrue();
        dto.FirstStart.Should().Be(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Preview_WithoutCreatingPlaceholders_ReportsTheUnknownMemberPerRow()
    {
        var preview = await PreviewAsync(Cups, Purchases);

        preview.Value.CanCommit.Should().BeFalse();
        preview.Value.RowErrors.Select(r => (r.Block, r.LineNumber)).Should().BeEquivalentTo(new[] { ("cups", 5), ("purchases", 4) });
    }

    [Fact]
    public async Task Preview_ReportsBadRows()
    {
        const string cups = """
            A;2025-01-01;2025-01-31;Alice;ten
            A;2025-01-02;2025-01-31;Admin;1
            B;2025-02-28;2025-02-01;Admin;1
            C;2025-03-01;2025-03-31;Admin
            D;2025-04-01;2025-04-30;Alice;1
            D;2025-04-01;2025-04-30;alice@example.org;2
            """;
        const string purchases = """
            Z;2025-01-01;;Beans;;10
            D;2025-04-01;;Beans;-5;10
            D;2025-04-01;;;;10
            D;2025-04-01;;Beans;;0
            """;

        var dto = (await PreviewAsync(cups, purchases)).Value;

        dto.CanCommit.Should().BeFalse();
        dto.RowErrors.Select(r => (r.Block, r.LineNumber)).Should().BeEquivalentTo(new[]
        {
            ("cups", 1), ("cups", 2), ("cups", 3), ("cups", 4), ("cups", 6),
            ("purchases", 1), ("purchases", 2), ("purchases", 3), ("purchases", 4)
        });
    }

    [Fact]
    public async Task Import_IsRefused_WithoutALedgerStartDate()
    {
        _world.Space.SetLedgerStartDate(null, hasLedgerSettlement: false);

        (await PreviewAsync(Cups, Purchases, create: true)).Value.Errors.Should().ContainSingle(e => e.Contains("ledger start"));
        var commit = await CommitAsync(Cups, Purchases, create: true);

        commit.Errors.Should().Contain(e => e.Code == Error.Codes.HistoryImportInvalid);
        _world.Periods.Should().BeEmpty();
        _world.Imports.Should().BeEmpty();
    }

    [Theory]
    [InlineData("2026-01-01")]
    [InlineData("2026-02-15")]
    public async Task Import_IsRefused_WhenAPeriodEndsOnOrAfterTheLedgerStart(string end)
    {
        var commit = await CommitAsync($"Late;2025-12-01;{end};Alice;3");

        commit.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.HistoryImportInvalid);
        _world.Periods.Should().BeEmpty();
    }

    [Fact]
    public async Task Commit_SavesHistoricalSettledPeriods_AndCreatesPlaceholders_WithoutTouchingMoney()
    {
        _world.AddEntry(_alice, -30m, LedgerStart);
        var live = _world.AddPeriod("Overlapping live", new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc), LedgerStart.AddDays(30), BillingState.Open);
        var before = (await _world.SnapshotService.RunningAsync(_world.Space, default)).Calculation.Lines
            .ToDictionary(l => l.UserId, l => l.Net);
        var version = _world.Space.LedgerVersion;

        var result = await CommitAsync(Cups, Purchases, create: true, note: "From the Excel");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Match<HistoryImportResultDto>(r => r.PeriodCount == 3 && r.TotalCups == 208 && r.TotalCost == 820.50m);
        var historical = _world.Periods.Where(p => p.IsHistorical).ToList();
        historical.Should().HaveCount(3).And.OnlyContain(p =>
            p.State == BillingState.Settled && p.HistoryImportId!.Value.Value == result.Value.ImportId);
        historical.Single(p => p.Name == "2025-12-31").EndDate.Should().Be(new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc).AddTicks(9_999_999));
        _world.Periods.Should().Contain(live, "overlap with a live period is accepted");

        var pete = _world.Space.Members.Select(m => _world.UserOf(m.UserId)).Single(u => u.Name == "Old Pete");
        pete.IsPlaceholder.Should().BeTrue();
        var import = _world.Imports.Single();
        import.Note.Should().Be("From the Excel");
        import.CupCounts.Should().HaveCount(4);
        import.CupCounts.Where(c => c.UserId == _alice).Sum(c => c.Cups).Should().Be(125);
        import.Purchases.Single(p => p.Description == "Beans").PaidByUserId.Should().Be(pete.Id);
        import.Purchases.Single(p => p.Description == "Milk and sugar").Should().Match<BeanShare.Domain.Aggregates.History.HistoricalPurchase>(
            p => p.PurchasedAt == null && p.PaidByUserId == null && p.Grams == null && p.Currency == "CZK");
        _world.Space.LedgerVersion.Should().BeGreaterThan(version);

        var after = (await _world.SnapshotService.RunningAsync(_world.Space, default)).Calculation.Lines
            .Where(l => l.Net != 0).ToDictionary(l => l.UserId, l => l.Net);
        after.Should().BeEquivalentTo(before.Where(kv => kv.Value != 0).ToDictionary(kv => kv.Key, kv => kv.Value), "history never touches money");
    }

    [Fact]
    public async Task Delete_RemovesOnlyThatImportsPeriods()
    {
        var first = (await CommitAsync("A;2025-01-01;2025-01-31;Alice;1")).Value.ImportId;
        var second = (await CommitAsync("B;2025-02-01;2025-02-28;Alice;2")).Value.ImportId;
        var live = _world.AddPeriod("January", LedgerStart, LedgerStart.AddDays(30), BillingState.Open);

        (await DeleteAsync(first)).IsSuccess.Should().BeTrue();

        _world.Imports.Select(i => i.Id.Value).Should().Equal(second);
        _world.Periods.Select(p => p.Name).Should().BeEquivalentTo("B", "January");
        _world.Periods.Should().Contain(live);
        (await DeleteAsync(first)).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.NotFound);
    }

    [Fact]
    public async Task TheLedgerStart_CannotMoveIntoTheHistory()
    {
        (await CommitAsync("A;2025-01-01;2025-06-30;Alice;1")).IsSuccess.Should().BeTrue();
        var handler = new UpdateLedgerSettingsHandler(
            _world.SpaceRepository, _world.SettlementRepository, _world.UserService, _world.UserRepository, _world.BillingPeriodRepository, _world.CoffeeStockRepository);

        var inside = await handler.Handle(new UpdateLedgerSettingsCommand(_world.Space.Id.Value, null, new DateTime(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc)), default);
        var after = await handler.Handle(new UpdateLedgerSettingsCommand(_world.Space.Id.Value, null, new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc)), default);

        inside.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ValidationError);
        after.IsSuccess.Should().BeTrue();
        _world.Space.LedgerStartDate.Should().Be(new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task HistoryView_ShowsImportedCups_PlusLiveCupsAfterTheHistory()
    {
        (await CommitAsync(Cups, Purchases, create: true)).IsSuccess.Should().BeTrue();
        var bag = _world.AddBag(1000m, 500m, LedgerStart);
        _world.Drink(_alice, 10m, LedgerStart.AddDays(2), bag);
        _world.Drink(_alice, 10m, LedgerStart.AddDays(3), bag);

        var result = await new GetSpaceHistoryHandler(_world.SpaceRepository, _world.HistoryImportRepository, _world.BillingPeriodRepository,
                _world.ConsumptionRepository, _world.CoffeeStockRepository, _world.UserService, _world.UserContext)
            .Handle(new GetSpaceHistoryQuery(_world.Space.Id.Value), default);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.IsAdmin.Should().BeTrue();
        dto.Periods.Should().HaveCount(3);
        dto.Imports.Should().ContainSingle().Which.Should().Match<HistoryImportSummaryDto>(i => i.PeriodCount == 3 && i.Cups == 208 && i.Cost == 820.50m);
        dto.MemberCups.Single(m => m.UserId == _alice.Value).Should().Match<HistoryMemberCupsDto>(m => m.HistoricalCups == 125 && m.LiveCups == 2);
        dto.Years.Should().Contain(y => y.Year == 2025 && y.Cost == 820.50m);
    }

    [Fact]
    public async Task AHistoricalPeriod_NeverBlocksGeneration()
    {
        // Even one ending after the generated period (bypassing the ledger-start rule) is ignored by the ordering guards.
        var live = _world.AddPeriod("January", LedgerStart, LedgerStart.AddDays(30));
        _world.Periods.Add(BillingPeriod.CreateHistorical(_world.Space.Id, "Odd", LedgerStart.AddDays(40), LedgerStart.AddDays(50),
            BeanShare.Domain.ValueObjects.HistoryImportId.New(), _world.Admin, _world.Clock));
        _world.Periods.Add(BillingPeriod.CreateHistorical(_world.Space.Id, "Old", LedgerStart.AddDays(-60), LedgerStart.AddDays(-30),
            BeanShare.Domain.ValueObjects.HistoryImportId.New(), _world.Admin, _world.Clock));

        (await _world.GenerateAsync(live)).IsSuccess.Should().BeTrue();
    }
    [Fact]
    public async Task APasteReachingTheCap_IsRefused()
    {
        var purchases = new string(' ', PasteLimits.MaxLength);

        (await PreviewAsync("A;2025-01-01;2025-01-31;Alice;1", purchases)).Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("may be cut");
        (await CommitAsync("A;2025-01-01;2025-01-31;Alice;1", purchases)).Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("may be cut");
        _world.Imports.Should().BeEmpty();
    }
}
