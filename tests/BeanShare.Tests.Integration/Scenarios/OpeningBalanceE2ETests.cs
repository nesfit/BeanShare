using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Xml.Linq;
using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Dtos;
using BeanShare.Application.Features.Billing.Dtos;
using BeanShare.Application.Features.History.Commands.CommitHistoryImport;
using BeanShare.Application.Features.History.Dtos;
using BeanShare.Application.Features.Settlement.Dtos;
using BeanShare.Application.Features.Spaces.Dtos;
using BeanShare.Application.Services;
using BeanShare.Contracts.CoffeeStock;
using BeanShare.Contracts.Spaces;
using BeanShare.Domain.Common;
using BeanShare.Domain.ValueObjects;
using BeanShare.Infrastructure.Persistence;
using BeanShare.Tests.Integration.Extensions;
using BeanShare.Tests.Integration.Fixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BeanShare.Tests.Integration.Scenarios;

/// <summary>
/// The opening-balance E2E scenario, from the Excel cutover through settlements, the
/// placeholder link and merge, to the history delete. Needs Docker (<see cref="PostgreSqlFixture"/>):
/// <c>nix develop -c dotnet test tests/BeanShare.Tests.Integration --filter "FullyQualifiedName~OpeningBalanceE2ETests"</c>.
/// Emails go to a capturing <see cref="IEmailService"/>. The scenario's arithmetic is checked Docker-free by
/// <see cref="OpeningBalanceScenarioTests"/>.
/// </summary>
public sealed class OpeningBalanceE2ETests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private readonly CapturingEmailService _emails = new();
    private readonly OpeningBalanceScenario _s = new(DateTime.UtcNow);
    private WebApplicationFactory<Program> _app = null!;

    private readonly Guid _h = Guid.NewGuid();
    private readonly Guid _a = Guid.NewGuid();
    private readonly Guid _b = Guid.NewGuid();
    private readonly Dictionary<int, Guid> _p = new();
    private HttpClient _asH = null!;
    private HttpClient _asA = null!;
    private HttpClient _asB = null!;
    private Guid _space;

    public OpeningBalanceE2ETests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _app = _fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(_emails);
            // The server's "now" is fixed to the scenario's day D at noon, so a run that crosses midnight UTC
            // still dates the 16e write-off shares inside L4, which ends on D.
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(new FixedClock(_s.D.AddHours(12)));
        }));

        await _fixture.EnsureUserAsync(_h, "h@example.org");
        await _fixture.EnsureUserAsync(_a, "a@example.org");
        await _fixture.EnsureUserAsync(_b, "b@example.org");
        _asH = _app.CreateClient().WithTestUser(_h, "h@example.org");
        _asA = _app.CreateClient().WithTestUser(_a, "a@example.org");
        _asB = _app.CreateClient().WithTestUser(_b, "b@example.org");
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task OpeningBalance_FromTheExcelCutover_ToAMergedPlaceholder()
    {
        DateTime C = _s.C, D = _s.D;

        // 1. H creates the space; A and B join.
        var created = await OkAsync<CreateSpaceResponse>(_asH.PostAsJsonAsync("/api/spaces", new CreateSpaceRequest { Name = "Kava", CurrencyCode = "CZK" }));
        _space = created.SpaceId;
        await OkAsync(_asA.PostAsJsonAsync("/api/spaces/join", new JoinSpaceRequest { InviteCode = created.InviteCode }));
        await OkAsync(_asB.PostAsJsonAsync("/api/spaces/join", new JoinSpaceRequest { InviteCode = created.InviteCode }));
        (await SpaceAsync()).Members.Should().HaveCount(3);

        // 2. 27 placeholders; P01 and P03 have an email.
        for (var nn = 1; nn <= OpeningBalanceScenario.PlaceholderCount; nn++)
        {
            var placeholder = await OkAsync<MembershipDto>(_asH.PostAsJsonAsync($"/api/spaces/{_space}/placeholders",
                new { Name = OpeningBalanceScenario.PlaceholderName(nn), Email = OpeningBalanceScenario.PlaceholderEmail(nn) }));
            _p[nn] = placeholder.UserId;
        }

        var space = await SpaceAsync();
        space.Members.Should().HaveCount(30);
        space.Members.Count(m => m.IsPlaceholder).Should().Be(27);

        // 3. Bags X (H pays) and Y (B pays).
        var x = await AddBagAsync("Alfa", 500m, C.AddDays(-10), "Shop X", paidBy: null);
        var y = await AddBagAsync("Beta", 700m, C.AddDays(5), "Shop Y", paidBy: _b);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var stock = await scope.ServiceProvider.GetRequiredService<ICoffeeStockRepository>().GetBySpaceIdAsync(new SpaceId(_space));
            stock!.Purchases.Single(p => p.Id == y).PaidByUserId.Should().Be(new UserId(_b));
        }

        // 4. A cup before C, then the ledger settings.
        await LogCupAsync(_a, x, "Alfa", _s.AtC(-2));
        (await OkAsync<LedgerStartImpactDto>(_asH.GetAsync($"/api/spaces/{_space}/ledger-settings/impact?ledgerStartDate={C:yyyy-MM-dd}")))
            .CupsBeforeStart.Should().Be(1);
        await OkAsync(_asH.PutAsJsonAsync($"/api/spaces/{_space}/ledger-settings", new { TreasurerUserId = _h, LedgerStartDate = C }));
        (await FailsAsync(LogCupResponseAsync(_a, x, "Alfa", _s.AtC(-1)), HttpStatusCode.BadRequest)).Should().Contain(Error.Codes.BeforeLedgerStart);

        // 5. The history paste.
        var history = new { CupsText = _s.HistoryCups, PurchasesText = _s.HistoryPurchases, Note = "kava.xlsx" };
        var historyPreview = await OkAsync<HistoryImportPreviewDto>(_asH.PostAsJsonAsync($"/api/spaces/{_space}/history-imports/preview", history));
        historyPreview.Periods.Should().HaveCount(OpeningBalanceScenario.HistoryPeriods);
        historyPreview.TotalCups.Should().Be(OpeningBalanceScenario.HistoryCupsTotal);
        historyPreview.TotalCost.Should().Be(OpeningBalanceScenario.HistoryCostTotal);
        historyPreview.PlaceholdersToCreate.Should().BeEmpty();
        historyPreview.RowErrors.Should().BeEmpty();
        historyPreview.Errors.Should().BeEmpty();
        var import = await OkAsync<HistoryImportResultDto>(_asH.PostAsJsonAsync($"/api/spaces/{_space}/history-imports", history));
        (await PeriodsAsync()).Where(p => p.IsHistorical).Should().HaveCount(4).And.OnlyContain(p => p.State == "Settled");
        (await UnsettledAsync(_asH)).Members.Should().OnlyContain(m => m.Balance == 0m);

        // 6. The opening balances of the final Excel round.
        var paste = new { Text = OpeningBalanceScenario.OpeningPaste, BatchNote = "Excel cutoff", CounterpartyUserId = _h, EffectiveDate = C, Kind = "Opening" };
        var pastePreview = await OkAsync<BalancePastePreviewDto>(_asH.PostAsJsonAsync($"/api/spaces/{_space}/balance-entries/paste/preview", paste));
        pastePreview.Rows.Should().HaveCount(OpeningBalanceScenario.OpeningRows);
        pastePreview.Total.Should().Be(OpeningBalanceScenario.OpeningTotal);
        pastePreview.CounterpartyResultingBalance.Should().Be(4198.00m);
        await OkAsync(_asH.PostAsJsonAsync($"/api/spaces/{_space}/balance-entries/paste", paste));

        // 7. L1 and its cups.
        var l1 = await OpenPeriodAsync("L1", C, C.AddDays(29));
        for (var day = 1; day <= 10; day++)
            await LogCupAsync(_a, x, "Alfa", _s.AtC(day));
        for (var i = 0; i < 5; i++)
            await LogCupAsync(_b, x, "Alfa", _s.AtC(2));
        for (var i = 0; i < 4; i++)
            await LogCupAsync(_a, y, "Beta", _s.AtC(6));
        for (var i = 0; i < 6; i++)
            await LogCupAsync(_h, y, "Beta", _s.AtC(7));
        await OkAsync(_asH.PutAsJsonAsync($"/api/billing-periods/{l1}/close", new { }));
        (await ReadAsync(db => db.Consumptions.CountAsync(c => c.BillingPeriodId == new BillingPeriodId(l1)))).Should().Be(25, "the close persists the stamps");

        // 8. The running balances: H sees everyone, A only their own.
        var unsettledH = await UnsettledAsync(_asH);
        BalanceOf(unsettledH, _a).Should().Be(-616.00m);
        BalanceOf(unsettledH, _b).Should().Be(165.00m);
        BalanceOf(unsettledH, _h).Should().Be(4231.00m);
        foreach (var (nn, id) in _p)
            BalanceOf(unsettledH, id).Should().Be(OpeningBalanceScenario.OpeningAmountOf(nn));
        var unsettledA = await UnsettledAsync(_asA);
        unsettledA.Members.Should().BeEmpty();
        unsettledA.MyBalance.Should().Be(-616.00m);

        // 9. S1.
        var s1 = await GenerateAsync(l1);
        s1.Lines.Should().HaveCount(30);
        LineOf(s1, _a).Should().Match<SettlementLineDto>(l =>
            l.AmountDue == 78.00m && (l.Credit ?? 0) == 0m && l.Adjustments == -538.00m && l.NetBalance == -616.00m && l.TotalCoffeeGrams == 140m,
            "the C−2 cup is not included");
        LineOf(s1, _b).Should().Match<SettlementLineDto>(l => l.AmountDue == 25.00m && l.Credit == 70.00m && l.Adjustments == 120.00m && l.NetBalance == 165.00m);
        LineOf(s1, _h).Should().Match<SettlementLineDto>(l =>
            l.AmountDue == 42.00m && l.Credit == 75.00m && l.Adjustments == 4198.00m && (l.Rounding ?? 0) == 0m && l.NetBalance == 4231.00m);
        foreach (var (nn, id) in _p)
            LineOf(s1, id).NetBalance.Should().Be(OpeningBalanceScenario.OpeningAmountOf(nn));
        s1.Lines.Sum(l => l.NetBalance).Should().Be(0m);
        // The exact star plan: A→H 616.00, H→B 165.00 and PNN→H 10·NN, nothing else.
        s1.Transfers.Should().BeEquivalentTo(
            new[] { new { FromUserId = _a, ToUserId = _h, Amount = 616.00m }, new { FromUserId = _h, ToUserId = _b, Amount = 165.00m } }
                .Concat(_p.Select(p => new { FromUserId = p.Value, ToUserId = _h, Amount = 10m * p.Key })));
        var balances = await OkAsync<SpaceBalancesDto>(_asH.GetAsync($"/api/spaces/{_space}/balances"));
        balances.Entries.Should().HaveCount(29).And.OnlyContain(e => e.IsLocked);
        var locked = balances.Entries.First(e => e.UserId == _a);
        (await FailsAsync(_asH.PutAsJsonAsync($"/api/spaces/{_space}/balance-entries/{locked.Id}", new
        {
            UserId = _a, CounterpartyUserId = _h, Amount = -500m, EffectiveDate = C, Note = "edit", Kind = "Opening"
        }), HttpStatusCode.BadRequest)).Should().Contain(Error.Codes.EntryLocked);

        // 10. Confirmations: A confirms own line; H confirms B and P01..P26; P27 stays open.
        await OkAsync(_asA.PutAsJsonAsync($"/api/settlements/{s1.Id}/confirm-payment/{_a}", new { }));
        await OkAsync(_asH.PutAsJsonAsync($"/api/settlements/{s1.Id}/confirm-payment/{_b}", new { }));
        for (var nn = 1; nn <= 26; nn++)
            await OkAsync(_asH.PutAsJsonAsync($"/api/settlements/{s1.Id}/confirm-payment/{_p[nn]}", new { }));
        s1 = await SettlementAsync(s1.Id);
        s1.Status.Should().Be("AwaitingConfirmation");
        LineOf(s1, _h).Resolution.Should().Be("Open");
        var afterS1 = await UnsettledAsync(_asH);
        BalanceOf(afterS1, _p[27]).Should().Be(-270.00m);
        BalanceOf(afterS1, _h).Should().Be(270.00m);

        // 10b. Exports, and the S1 emails: exactly H, A, B, P01 and P03.
        await PdfExportAsync(s1.Id);
        var excel = await _asH.GetAsync($"/api/settlements/{s1.Id}/export/excel");
        excel.StatusCode.Should().Be(HttpStatusCode.OK);
        // The header row of the ledger lines sheet; the Adjustments column is headed "Adj." as built.
        var details = ExcelSheetRows(await excel.Content.ReadAsByteArrayAsync(), "Details");
        details.Should().Contain(row => row.Count > 0 && row[0] == "Member")
            .Which.Should().Contain(new[] { "Credit", "Carried", "Adj.", "Moved (merge)", "Rounding", "Net" });
        (await OkAsync<EmailsSentResponse>(_asH.PostAsJsonAsync($"/api/settlements/{s1.Id}/send-emails", new { }))).EmailsSent.Should().Be(5);
        _emails.TakeRecipients().Should().BeEquivalentTo("h@example.org", "a@example.org", "b@example.org", "p01@example.org", "p03@example.org");

        // 11. L2 and L3; generating L3 first is refused.
        var l2 = await OpenPeriodAsync("L2", C.AddDays(30), C.AddDays(49));
        var l3 = await OpenPeriodAsync("L3", C.AddDays(50), C.AddDays(59));
        for (var i = 0; i < 2; i++)
            await LogCupAsync(_a, y, "Beta", _s.AtC(31));
        await OkAsync(_asH.PostAsJsonAsync($"/api/spaces/{_space}/balance-entries", new
        {
            UserId = _p[5], CounterpartyUserId = _h, Amount = 50.00m, EffectiveDate = C.AddDays(35), Note = "refund", Kind = "Correction"
        }));
        await OkAsync(_asH.PutAsJsonAsync($"/api/billing-periods/{l2}/close", new { }));
        await OkAsync(_asH.PutAsJsonAsync($"/api/billing-periods/{l3}/close", new { }));
        (await FailsAsync(_asH.PostAsJsonAsync($"/api/billing-periods/{l3}/settlement", new { }), HttpStatusCode.BadRequest))
            .Should().Contain(Error.Codes.SettlementOrder);

        // 12. S2.
        var s2 = await GenerateAsync(l2);
        LineOf(s2, _a).NetBalance.Should().Be(-14.00m);
        LineOf(s2, _b).NetBalance.Should().Be(14.00m);
        LineOf(s2, _p[5]).NetBalance.Should().Be(50.00m);
        LineOf(s2, _p[27]).Should().Match<SettlementLineDto>(l => l.CarriedIn == -270.00m && l.NetBalance == -270.00m);
        LineOf(s2, _h).Should().Match<SettlementLineDto>(l => l.Adjustments == -50.00m && l.CarriedIn == 270.00m && l.NetBalance == 220.00m);
        s2.Lines.Sum(l => l.NetBalance).Should().Be(0m);
        s1 = await SettlementAsync(s1.Id);
        LineOf(s1, _p[27]).Resolution.Should().Be("Carried");
        LineOf(s1, _h).Resolution.Should().Be("Carried");
        s1.SupersededBySettlementId.Should().Be(s2.Id);

        // 13. A carried line is confirmed where it was carried to (LINE_CARRIED).
        (await FailsAsync(_asH.PutAsJsonAsync($"/api/settlements/{s1.Id}/confirm-payment/{_p[27]}", new { }), HttpStatusCode.BadRequest))
            .Should().Contain(Error.Codes.LineCarried);

        // 14. S3 (no cups) carries S2's nets, and has exactly those five lines; S2 is superseded.
        var s3 = await GenerateAsync(l3);
        var carried = new Dictionary<Guid, decimal> { [_a] = -14.00m, [_b] = 14.00m, [_p[5]] = 50.00m, [_p[27]] = -270.00m, [_h] = 220.00m };
        foreach (var (who, net) in carried)
        {
            LineOf(s3, who).CarriedIn.Should().Be(net);
            LineOf(s3, who).NetBalance.Should().Be(net);
        }

        s3.Lines.Select(l => l.UserId).Should().BeEquivalentTo(carried.Keys, "members with nothing carried get no line");
        s3.Lines.Sum(l => l.NetBalance).Should().Be(0m);

        (await SettlementAsync(s2.Id)).SupersededBySettlementId.Should().Be(s3.Id);
        s3.Lines.Sum(l => l.TotalCoffeeGrams).Should().Be(0m, "neither the C−2 cup nor any history is ever included");

        // 15. A superseded settlement still exports, but its emails are refused (the 400 shape as built).
        await PdfExportAsync(s1.Id);
        (await FailsAsync(_asH.PostAsJsonAsync($"/api/settlements/{s1.Id}/send-emails", new { }), HttpStatusCode.BadRequest))
            .Should().Contain("Cannot send emails for settlement in carried-forward state");
        _emails.TakeRecipients().Should().BeEmpty();

        // 16. Sync: a) P01 links and claims; b) an unverified P03 is refused; c) a ghost cannot join.
        (await SyncAsync("kc-p01", "p01@example.org", "True")).Should().BeOfType<UserSyncOutcome.PlaceholderLinkPending>()
            .Which.PlaceholderUserId.Should().Be(_p[1]);
        (await ResolveAsync("kc-p01", "p01@example.org")).Should().Be(LinkResolveOutcome.Claimed);
        (await SyncAsync("kc-p01", "p01@example.org", "True")).Should().BeOfType<UserSyncOutcome.Synced>()
            .Which.User.Id.Should().Be(new UserId(_p[1]));
        var p01 = await ReadAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == new UserId(_p[1])));
        p01.IsPlaceholder.Should().BeFalse();
        LineOf(await SettlementAsync(s1.Id), _p[1]).NetBalance.Should().Be(-10.00m, "P01's lines are intact");
        (await OkAsync<SpaceBalancesDto>(_asH.GetAsync($"/api/spaces/{_space}/balances"))).Entries.Should().Contain(e => e.UserId == _p[1]);

        (await SyncAsync("kc-p03", "p03@example.org", "false")).Should().Be(new UserSyncOutcome.Refused(Error.Codes.EmailNotVerified));
        (await ReadAsync(db => db.Users.AsNoTracking().CountAsync(u => u.Email == "p03@example.org"))).Should().Be(1);
        (await ReadAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == new UserId(_p[3])))).IsPlaceholder.Should().BeTrue();

        var ghost = _app.CreateClient().WithTestUser(Guid.NewGuid(), "ghost@example.org");
        var ghostJoin = await ghost.PostAsJsonAsync("/api/spaces/join", new JoinSpaceRequest { InviteCode = created.InviteCode });
        ghostJoin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ghostJoin.Content.ReadAsStringAsync()).Should().Contain(Error.Codes.Unauthenticated);

        // 16c2. A cup for P27 before the merge: 7.00, credited to B (Y's payer).
        (await LogCupResponseAsync(_p[27], y, "Beta", D.AddDays(-1).AddHours(9))).StatusCode.Should().Be(HttpStatusCode.Created);
        var beforeMerge = await UnsettledAsync(_asH);
        BalanceOf(beforeMerge, _p[27]).Should().Be(-277.00m);
        beforeMerge.Members.Single(m => m.UserId == _b).Credit.Should().Be(7.00m);
        var entriesBeforeMerge = (await OkAsync<SpaceBalancesDto>(_asH.GetAsync($"/api/spaces/{_space}/balances"))).Entries.Count;

        // 16d. Merge P27 into A: no entry, the sweep moves the money; P27 is hidden and refused everywhere.
        await OkAsync(_asH.PostAsJsonAsync($"/api/spaces/{_space}/placeholders/{_p[27]}/merge", new { TargetUserId = _a }));
        (await OkAsync<SpaceBalancesDto>(_asH.GetAsync($"/api/spaces/{_space}/balances"))).Entries.Should().HaveCount(entriesBeforeMerge, "no Correction entry");
        var afterMerge = await UnsettledAsync(_asH);
        BalanceOf(afterMerge, _p[27]).Should().Be(0m, "the merged placeholder is still a (hidden) member, swept to 0");
        BalanceOf(afterMerge, _a).Should().Be(-291.00m);
        var spaceHistory = await OkAsync<SpaceHistoryDto>(_asH.GetAsync($"/api/spaces/{_space}/history"));
        spaceHistory.MemberCups.Single(m => m.UserId == _a).HistoricalCups.Should().Be(55);
        (await SpaceAsync()).Members.Should().NotContain(m => m.UserId == _p[27]);
        (await FailsAsync(_asH.PutAsJsonAsync($"/api/settlements/{s3.Id}/confirm-payment/{_p[27]}", new { }), HttpStatusCode.BadRequest))
            .Should().Contain(Error.Codes.LineMerged);
        (await FailsAsync(LogCupResponseAsync(_p[27], y, "Beta", D.AddDays(-1).AddHours(10)), HttpStatusCode.BadRequest))
            .Should().Contain(Error.Codes.MemberMerged);
        (await FailsAsync(_asH.PostAsJsonAsync($"/api/spaces/{_space}/members/{_p[27]}/remove", new { SpaceId = _space, UserId = _p[27] }), HttpStatusCode.BadRequest))
            .Should().Contain(Error.Codes.MemberMerged);

        // 16e. Y is marked empty: 870 g shared by A and H (60 g each of Y); P27's cup gives no share.
        await OkAsync(_asH.PostAsJsonAsync($"/api/spaces/{_space}/stock/bags/{y}/empty", new MarkBagEmptyRequest()));
        var shares = await ReadAsync(db => db.Consumptions.AsNoTracking().Where(c => c.IsWriteOff && c.StockLotId == y).ToListAsync());
        shares.GroupBy(c => c.UserId.Value).ToDictionary(g => g.Key, g => g.Sum(c => c.Quantity.Grams))
            .Should().BeEquivalentTo(new Dictionary<Guid, decimal> { [_a] = 435m, [_h] = 435m });
        shares.Should().OnlyContain(c => c.CostAmount == 304.50m);

        // 16f. L4 ends today (the API takes an End date; the write-off shares above are dated now), then S4.
        var l4 = await OpenPeriodAsync("L4", C.AddDays(60), D);
        await OkAsync(_asH.PutAsJsonAsync($"/api/billing-periods/{l4}/close", new { }));
        var s4 = await GenerateAsync(l4);
        (await SettlementAsync(s3.Id)).SupersededBySettlementId.Should().Be(s4.Id);

        // 16g. S4: the sweep moves P27's whole balance to A.
        LineOf(s4, _a).Should().Match<SettlementLineDto>(l =>
            l.AmountDue == 304.50m && l.CarriedIn == -14.00m && l.MergeTransfer == -277.00m && l.NetBalance == -595.50m);
        LineOf(s4, _p[27]).Should().Match<SettlementLineDto>(l =>
            l.AmountDue == 7.00m && l.CarriedIn == -270.00m && l.MergeTransfer == 277.00m && l.NetBalance == 0m && l.Resolution == "Confirmed");
        LineOf(s4, _b).Should().Match<SettlementLineDto>(l => l.Credit == 616.00m && l.CarriedIn == 14.00m && l.NetBalance == 630.00m);
        LineOf(s4, _p[5]).NetBalance.Should().Be(50.00m);
        LineOf(s4, _h).Should().Match<SettlementLineDto>(l => l.AmountDue == 304.50m && l.CarriedIn == 220.00m && l.NetBalance == -84.50m);
        s4.Lines.Sum(l => l.NetBalance).Should().Be(0m);
        s4.Transfers.Should().BeEquivalentTo(new[]
        {
            new { FromUserId = _a, ToUserId = _h, Amount = 595.50m },
            new { FromUserId = _h, ToUserId = _b, Amount = 630.00m },
            new { FromUserId = _h, ToUserId = _p[5], Amount = 50.00m }
        });

        // 16h. B is paid out in cash; S5 then mails only H and A.
        await OkAsync(_asH.PostAsJsonAsync($"/api/spaces/{_space}/balance-entries", new
        {
            UserId = _b, CounterpartyUserId = _h, Amount = -630.00m, EffectiveDate = D, Note = "paid out in cash", Kind = "Correction"
        }));
        // L5 lies in the future on purpose: L4 had to end today because the 16e write-off shares are dated now,
        // and a live period must start after the last settled End. Nothing refuses a future period, and no cup is logged into it.
        var l5 = await OpenPeriodAsync("L5", D.AddDays(1), D.AddDays(2));
        await OkAsync(_asH.PutAsJsonAsync($"/api/billing-periods/{l5}/close", new { }));
        var s5 = await GenerateAsync(l5);
        (await SettlementAsync(s4.Id)).SupersededBySettlementId.Should().Be(s5.Id);
        LineOf(s5, _a).Should().Match<SettlementLineDto>(l => l.CarriedIn == -595.50m && l.NetBalance == -595.50m);
        LineOf(s5, _b).Should().Match<SettlementLineDto>(l =>
            l.CarriedIn == 630.00m && l.Adjustments == -630.00m && l.NetBalance == 0m && l.Resolution == "Confirmed");
        LineOf(s5, _p[5]).NetBalance.Should().Be(50.00m);
        LineOf(s5, _h).Should().Match<SettlementLineDto>(l => l.CarriedIn == -84.50m && l.Adjustments == 630.00m && l.NetBalance == 545.50m);
        s5.Lines.Should().NotContain(l => l.UserId == _p[27], "its S4 line was confirmed at 0");
        s5.Lines.Sum(l => l.NetBalance).Should().Be(0m);
        s5.Transfers.Should().BeEquivalentTo(new[]
        {
            new { FromUserId = _a, ToUserId = _h, Amount = 595.50m },
            new { FromUserId = _h, ToUserId = _p[5], Amount = 50.00m }
        });
        (await OkAsync<EmailsSentResponse>(_asH.PostAsJsonAsync($"/api/settlements/{s5.Id}/send-emails", new { }))).EmailsSent.Should().Be(2);
        _emails.TakeRecipients().Should().BeEquivalentTo("h@example.org", "a@example.org");

        // 17. Deleting the history import removes its 4 periods and changes no balance.
        var before = (await UnsettledAsync(_asH)).Members.ToDictionary(m => m.UserId, m => m.Balance);
        (await _asH.DeleteAsync($"/api/spaces/{_space}/history-imports/{import.ImportId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await PeriodsAsync()).Should().NotContain(p => p.IsHistorical);
        (await UnsettledAsync(_asH)).Members.ToDictionary(m => m.UserId, m => m.Balance).Should().BeEquivalentTo(before);
    }

    // ---- API helpers ----

    private async Task<GetSpaceByIdResponse> SpaceAsync() => await OkAsync<GetSpaceByIdResponse>(_asH.GetAsync($"/api/spaces/{_space}"));

    private async Task<List<BillingPeriodSummaryDto>> PeriodsAsync() =>
        await OkAsync<List<BillingPeriodSummaryDto>>(_asH.GetAsync($"/api/spaces/{_space}/billing-periods"));

    private async Task<UnsettledSpaceSummaryDto> UnsettledAsync(HttpClient client) =>
        await OkAsync<UnsettledSpaceSummaryDto>(client.GetAsync($"/api/spaces/{_space}/unsettled"));

    // A real PDF document, not just a 200: the content type, the "%PDF-" signature and more than a stub's length.
    private async Task PdfExportAsync(Guid settlementId)
    {
        var pdf = await _asH.GetAsync($"/api/settlements/{settlementId}/export/pdf");
        pdf.StatusCode.Should().Be(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(1024);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    private async Task<SettlementDto> SettlementAsync(Guid id) => await OkAsync<SettlementDto>(_asH.GetAsync($"/api/settlements/{id}"));

    private async Task<SettlementDto> GenerateAsync(Guid periodId) =>
        await OkAsync<SettlementDto>(_asH.PostAsJsonAsync($"/api/billing-periods/{periodId}/settlement", new { }));

    private async Task<Guid> OpenPeriodAsync(string name, DateTime start, DateTime end)
    {
        var period = await OkAsync<BillingPeriodDto>(_asH.PostAsJsonAsync($"/api/spaces/{_space}/billing-periods",
            new { SpaceId = _space, Name = name, StartDate = start, EndDate = end }));
        await OkAsync(_asH.PutAsJsonAsync($"/api/billing-periods/{period.Id}/open", new { }));
        return period.Id;
    }

    private async Task<Guid> AddBagAsync(string product, decimal cost, DateTime purchasedAt, string vendor, Guid? paidBy)
    {
        await OkAsync(_asH.PostAsJsonAsync($"/api/spaces/{_space}/stock/purchases", new AddStockPurchaseRequest
        {
            BodySpaceId = _space,
            ProductName = product,
            ProductBrand = "Roaster",
            ProductType = "Filter",
            QuantityGrams = 1000m,
            CostAmount = cost,
            CostCurrency = "CZK",
            Vendor = vendor,
            PurchasedAt = purchasedAt,
            PaidByUserId = paidBy
        }));
        var stock = await OkAsync<SpaceStockResponse>(_asH.GetAsync($"/api/spaces/{_space}/stock?pageSize=100"));
        return stock.RecentPurchases.Single(p => p.Vendor == vendor).Id;
    }

    /// <summary>H logs a 10 g cup for <paramref name="drinker"/> from <paramref name="bag"/>.</summary>
    private async Task LogCupAsync(Guid drinker, Guid bag, string product, DateTime at)
    {
        var response = await LogCupResponseAsync(drinker, bag, product, at);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private Task<HttpResponseMessage> LogCupResponseAsync(Guid drinker, Guid bag, string product, DateTime at) =>
        _asH.PostAsJsonAsync("/api/consumptions", new
        {
            SpaceId = _space,
            ProductName = product,
            ProductBrand = "Roaster",
            ProductType = "Filter",
            QuantityGrams = 10m,
            ConsumedAt = at,
            ForUserId = drinker == _h ? (Guid?)null : drinker,
            StockLotId = bag
        });

    private async Task<UserSyncOutcome> SyncAsync(string subject, string email, string verified)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IUserSynchronizationService>()
            .SyncFromClaimsAsync(Principal(subject, email, verified));
    }

    private async Task<LinkResolveOutcome> ResolveAsync(string subject, string email)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IUserSynchronizationService>()
            .ResolvePlaceholderLinkAsync(Principal(subject, email, "True"), accept: true);
    }

    private static ClaimsPrincipal Principal(string subject, string email, string verified) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim("sub", subject), new Claim("name", email.Split('@')[0]),
            new Claim(OidcClaims.Email, email), new Claim(OidcClaims.EmailVerified, verified)
        }, "oidc"));

    private async Task<T> ReadAsync<T>(Func<BeanShareDbContext, Task<T>> read)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<BeanShareDbContext>());
    }

    private static decimal BalanceOf(UnsettledSpaceSummaryDto summary, Guid user) => summary.Members.Single(m => m.UserId == user).Balance;

    private static SettlementLineDto LineOf(SettlementDto settlement, Guid user) => settlement.Lines.Single(l => l.UserId == user);

    private static async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.Should().BeTrue("{0} {1} answered {2}: {3}", response.RequestMessage?.Method, response.RequestMessage?.RequestUri?.PathAndQuery, (int)response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task OkAsync(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.Should().BeTrue("{0} {1} answered {2}: {3}", response.RequestMessage?.Method, response.RequestMessage?.RequestUri?.PathAndQuery, (int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> FailsAsync(Task<HttpResponseMessage> call, HttpStatusCode status)
    {
        var response = await call;
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);
        return body;
    }

    /// <summary>The cell texts, row by row, of the worksheet named <paramref name="sheetName"/> in an .xlsx.</summary>
    private static List<List<string>> ExcelSheetRows(byte[] xlsx, string sheetName)
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace pkg = "http://schemas.openxmlformats.org/package/2006/relationships";
        using var zip = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read);
        XDocument Load(string path)
        {
            using var stream = zip.GetEntry(path)!.Open();
            return XDocument.Load(stream);
        }

        var sheet = Load("xl/workbook.xml").Descendants(main + "sheet").Single(s => (string?)s.Attribute("name") == sheetName);
        var relId = (string)sheet.Attribute(rel + "id")!;
        var target = (string)Load("xl/_rels/workbook.xml.rels").Descendants(pkg + "Relationship")
            .Single(r => (string?)r.Attribute("Id") == relId).Attribute("Target")!;
        var sheetPath = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
        var shared = zip.GetEntry("xl/sharedStrings.xml") is null
            ? new List<string>()
            : Load("xl/sharedStrings.xml").Descendants(main + "si").Select(si => string.Concat(si.Descendants(main + "t").Select(t => t.Value))).ToList();

        return Load(sheetPath).Descendants(main + "row")
            .Select(row => row.Elements(main + "c").Select(c => (string?)c.Attribute("t") switch
            {
                "s" => c.Element(main + "v") is { } index
                    ? shared[int.Parse(index.Value, System.Globalization.CultureInfo.InvariantCulture)]
                    : "",
                "inlineStr" => string.Concat(c.Descendants(main + "t").Select(t => t.Value)),
                _ => c.Element(main + "v")?.Value ?? ""
            }).ToList())
            .ToList();
    }

    private sealed record EmailsSentResponse(int EmailsSent, string Message);

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow { get; } = now;
    }

    /// <summary>Records who would get an email; every send succeeds.</summary>
    private sealed class CapturingEmailService : IEmailService
    {
        private readonly ConcurrentQueue<string> _recipients = new();

        public List<string> TakeRecipients()
        {
            var taken = new List<string>();
            while (_recipients.TryDequeue(out var recipient))
                taken.Add(recipient);
            return taken;
        }

        public Task SendEmailAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            _recipients.Enqueue(message.To);
            return Task.CompletedTask;
        }

        public Task SendEmailWithAttachmentAsync(EmailMessage message, EmailAttachment attachment, CancellationToken cancellationToken = default) =>
            SendEmailAsync(message, cancellationToken);

        public Task SendEmailWithAttachmentsAsync(EmailMessage message, IEnumerable<EmailAttachment> attachments, CancellationToken cancellationToken = default) =>
            SendEmailAsync(message, cancellationToken);

        public Task<IReadOnlyList<EmailSendResult>> SendBatchAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken = default) =>
            SendBatchAsync(messages, [], cancellationToken);

        public Task<IReadOnlyList<EmailSendResult>> SendBatchAsync(
            IReadOnlyList<EmailMessage> messages, IReadOnlyList<EmailAttachment> sharedAttachments, CancellationToken cancellationToken = default)
        {
            foreach (var message in messages)
                _recipients.Enqueue(message.To);
            return Task.FromResult<IReadOnlyList<EmailSendResult>>(messages.Select(m => new EmailSendResult(m.To, true)).ToList());
        }
    }
}
