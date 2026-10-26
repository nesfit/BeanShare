using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Settlement.Commands.SendSettlementEmails;
using BeanShare.Application.Features.Settlement.Dtos;
using BeanShare.Application.Features.Settlement.Queries.GetSettlementReportData;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BeanShare.Application.Tests.Features.Settlement;

// T-EML-01 (D25, DS-40; Y5): settlement emails go only to people with something to read or do,
// worded by the line's state when the mail is sent.
public sealed class SendSettlementEmailsRecipientTests
{
    private const string Treasurer = "Tess";
    private static readonly DateTime Feb1 = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb10 = new(2026, 2, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb28 = new(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc);

    private readonly LedgerWorld _world = new("CZK", new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc));
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IEmailService _emailService = Substitute.For<IEmailService>();
    private readonly ISettlementEmailTemplateService _templates = Substitute.For<ISettlementEmailTemplateService>();
    private readonly ISettlementPdfGenerator _pdf = Substitute.For<ISettlementPdfGenerator>();
    private readonly List<SettlementEmailModel> _models = new();
    private readonly List<EmailMessage> _sent = new();
    private readonly List<IReadOnlyList<EmailAttachment>> _attachments = new();

    public SendSettlementEmailsRecipientTests()
    {
        _templates.GenerateSettlementEmail(Arg.Any<SettlementEmailModel>(), Arg.Any<string?>())
            .Returns(ci =>
            {
                var model = ci.Arg<SettlementEmailModel>();
                _models.Add(model);
                return (model.Headline, model.Headline);
            });
        _pdf.Generate(Arg.Any<SettlementReportData>()).Returns([1, 2, 3]);
        _emailService.SendBatchAsync(Arg.Any<IReadOnlyList<EmailMessage>>(), Arg.Any<IReadOnlyList<EmailAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var messages = ci.ArgAt<IReadOnlyList<EmailMessage>>(0);
                _sent.AddRange(messages);
                _attachments.Add(ci.ArgAt<IReadOnlyList<EmailAttachment>>(1));
                return (IReadOnlyList<EmailSendResult>)messages.Select(m => new EmailSendResult(m.To, true)).ToList();
            });
    }

    [Fact]
    public async Task Settlement1_MailsTheFourLinesWithSomethingToReadOrDo()
    {
        var result = await SendAsync(Ledger(
            Line("Drew", "drew@example.org", grams: 20m, net: 0m),
            Line("Dana", "dana@example.org", net: -10m),
            Line("Cleo", "cleo@example.org", net: 40m),
            Line("Quinn", "quinn@example.org", net: 0m, carriedIn: -20m, adjustments: 20m),
            Line("Placeholder 01", "", net: -30m),
            Line("Placeholder 27", "", net: 0m),
            Line(Treasurer, "tess@example.org", net: 0m, isTreasurer: true)));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(4);
        _sent.Select(m => m.To).Should().BeEquivalentTo(
            "drew@example.org", "dana@example.org", "cleo@example.org", "tess@example.org");
        _attachments.Should().ContainSingle().Which.Should().ContainSingle("every recipient gets the same full PDF");
        _pdf.Received(1).Generate(Arg.Any<SettlementReportData>());

        Headline("Drew").Should().Be("nothing to pay");
        Headline("Dana").Should().Be("you pay Tess 10.00 CZK");
        Headline("Cleo").Should().Be("Tess pays you 40.00 CZK");
        Headline(Treasurer).Should().Contain("you collect 40.00 CZK").And.Contain("you pay out 40.00 CZK");
        _models.Single(m => m.UserName == Treasurer).TreasurerTransfers.Should().BeEquivalentTo(
            "Dana pays you 10.00 CZK", "you pay Cleo 40.00 CZK", "Placeholder 01 pays you 30.00 CZK");
    }

    [Fact]
    public async Task ConfirmedLines_ReadAsPaidAndConfirmed()
    {
        await SendAsync(Ledger(
            Line("Dana", "dana@example.org", net: -10m, resolution: "Confirmed"),
            Line("Cleo", "cleo@example.org", net: 10m, resolution: "Confirmed"),
            Line(Treasurer, "tess@example.org", net: 0m, isTreasurer: true)));

        Headline("Dana").Should().Be("you paid Tess 10.00 CZK — confirmed, thank you");
        Headline("Cleo").Should().Be("Tess paid you 10.00 CZK — confirmed");
        Headline(Treasurer).Should().Be("all payments through you are confirmed");
    }

    // Code round 3, G8: the treasurer's headline counts only what is still open.
    [Fact]
    public async Task TheTreasurerHeadline_CountsOnlyOpenPayments()
    {
        await SendAsync(Ledger(
            Line("Dana", "dana@example.org", net: -10m, resolution: "Confirmed"),
            Line("Erin", "erin@example.org", net: -15m),
            Line("Cleo", "cleo@example.org", net: 25m),
            Line(Treasurer, "tess@example.org", net: 0m, isTreasurer: true)));

        Headline(Treasurer).Should().Be("you collect 15.00 CZK and you pay out 25.00 CZK as treasurer");
    }

    // Code round 3, G13: amounts use the currency's own decimal places.
    [Theory]
    [InlineData("JPY", "you pay Tess 1,000 JPY")]
    [InlineData("KWD", "you pay Tess 1,000.000 KWD")]
    public async Task Amounts_UseTheCurrencysDecimalPlaces(string currency, string expected)
    {
        await SendAsync(Ledger(
            Line("Dana", "dana@example.org", net: -1000m),
            Line(Treasurer, "tess@example.org", net: 1000m, isTreasurer: true)) with { Currency = currency });

        Headline("Dana").Should().Be(expected);
        _models.Should().AllSatisfy(m => m.CurrencyDecimals.Should().Be(currency == "JPY" ? 0 : 3));
    }

    // Code round 3, G11: only a space admin can send the settlement emails.
    [Fact]
    public async Task ANonAdmin_CannotSendTheEmails()
    {
        var settlementId = await SettlementIdAsync();
        _world.Actor = _world.AddMember("Mia");

        var result = await SendAsync(Ledger(Line("Dana", "dana@example.org", net: -10m)), settlementId);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.NotAdmin);
        _sent.Should().BeEmpty();
        _ = _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<GetSettlementReportDataQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Settlement2_OnlyTheTreasurerWithNothingToDo_SendsNothing()
    {
        var result = await SendAsync(Ledger(Line(Treasurer, "tess@example.org", net: 0m, isTreasurer: true)));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
        _sent.Should().BeEmpty();
        _attachments.Should().BeEmpty("no batch is sent at all");
    }

    [Fact]
    public async Task ALegacySettlement_MailsEveryLineWithAnEmail()
    {
        var report = Ledger(
            Line("Drew", "drew@example.org", grams: 20m) with { Net = null, AmountDue = 12.5m },
            Line("Erin", "erin@example.org", grams: 5m) with { Net = null, AmountDue = 3m },
            Line("Ghost", "", grams: 5m) with { Net = null }) with { IsLedger = false, TreasurerName = null };

        var result = await SendAsync(report);

        result.Value.Should().Be(2);
        Headline("Drew").Should().Be("Amount due: 12.50 CZK");
        _models.Should().AllSatisfy(m => m.IsLedger.Should().BeFalse());
    }

    // N-2: a settlement whose lines were carried into the next one is history.
    [Fact]
    public async Task ACarriedForwardSettlement_IsRefused()
    {
        var report = Ledger(Line("Dana", "dana@example.org", net: -10m)) with { IsCarriedForward = true };

        var result = await SendAsync(report);

        result.IsFailure.Should().BeTrue();
        result.Errors.Single().Code.Should().Be(Error.Codes.InvalidSettlementState);
        _sent.Should().BeEmpty();
    }

    private async Task<Result<int>> SendAsync(SettlementReportData report, SettlementId? settlementId = null)
    {
        var id = settlementId ?? await SettlementIdAsync();
        _mediator.Send(Arg.Any<GetSettlementReportDataQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<SettlementReportData>.Success(report));

        return await new SendSettlementEmailsHandler(_mediator, _emailService, _templates, _pdf,
                _world.SettlementRepository, _world.SpaceRepository, _world.UserContext, NullLogger<SendSettlementEmailsHandler>.Instance)
            .Handle(new SendSettlementEmailsCommand(id), default);
    }

    /// <summary>A real settlement of the space, for the admin check; the report itself comes from the canned mediator.</summary>
    private async Task<SettlementId> SettlementIdAsync()
    {
        if (_world.Settlements.Count == 0)
        {
            _world.Space.SetTreasurer(_world.Admin);
            var bag = _world.AddBag(1000m, 500m, Feb10.AddDays(-1));
            _world.Drink(_world.Admin, 10m, Feb10, bag);
            (await _world.GenerateAsync(_world.AddPeriod("February", Feb1, Feb28))).IsSuccess.Should().BeTrue();
        }

        return _world.Settlements.Single().Id;
    }

    private string Headline(string userName) => _models.Single(m => m.UserName == userName).Headline;

    private static SettlementReportData Ledger(params SettlementLineReportData[] lines) => new(
        "Kava", "February", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), "Admin",
        lines.Sum(l => l.AmountDue), "CZK", lines.Length, lines.Sum(l => l.CoffeeGrams), 0m, lines,
        IsLedger: true, TreasurerName: Treasurer);

    private static SettlementLineReportData Line(
        string name, string email, decimal grams = 0m, decimal? net = 0m, decimal carriedIn = 0m, decimal adjustments = 0m,
        string resolution = "Open", bool isTreasurer = false) =>
        new(name, email, grams, null, 0m, 0m, resolution == "Confirmed", null, null,
            Credit: 0m, CarriedIn: carriedIn, Adjustments: adjustments, Rounding: 0m, Net: net,
            Resolution: resolution, IsTreasurer: isTreasurer);
}
