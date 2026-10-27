using System.Globalization;
using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Settlement.Dtos;
using BeanShare.Application.Features.Settlement.Queries.GetSettlementReportData;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BeanShare.Application.Features.Settlement.Commands.SendSettlementEmails;
public sealed class SendSettlementEmailsHandler : IRequestHandler<SendSettlementEmailsCommand, Result<int>>
{
    private readonly IMediator _mediator;
    private readonly IEmailService _emailService;
    private readonly ISettlementEmailTemplateService _templateService;
    private readonly ISettlementPdfGenerator _pdfGenerator;
    private readonly ISettlementRepository _settlementRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;
    private readonly ILogger<SendSettlementEmailsHandler> _logger;

    public SendSettlementEmailsHandler(
        IMediator mediator,
        IEmailService emailService,
        ISettlementEmailTemplateService templateService,
        ISettlementPdfGenerator pdfGenerator,
        ISettlementRepository settlementRepository,
        ISpaceRepository spaceRepository,
        IUserContext userContext,
        ILogger<SendSettlementEmailsHandler> logger)
    {
        _settlementRepository = settlementRepository;
        _spaceRepository = spaceRepository;
        _userContext = userContext;
        _mediator = mediator;
        _emailService = emailService;
        _templateService = templateService;
        _pdfGenerator = pdfGenerator;
        _logger = logger;
    }

    public async Task<Result<int>> Handle(SendSettlementEmailsCommand command, CancellationToken cancellationToken)
    {
        var settlement = await _settlementRepository.GetByIdAsync(command.SettlementId, cancellationToken);
        if (settlement is null)
            return Result<int>.Failure(Error.SettlementNotFound(command.SettlementId.Value));

        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(settlement.SpaceId), cancellationToken);
        if (space is null)
            return Result<int>.Failure(Error.SpaceNotFound(settlement.SpaceId.Value));
        if (!space.IsAdmin(_userContext.CurrentUserId))
            return Result<int>.Failure(Error.NotAdmin());

        var reportDataResult = await _mediator.Send(new GetSettlementReportDataQuery(command.SettlementId), cancellationToken);

        if (!reportDataResult.IsSuccess)
            return Result<int>.Failure(reportDataResult.Errors.ToArray());

        var reportData = reportDataResult.Value;

        // A carried-forward settlement is history: its lines live on in the next settlement.
        if (reportData.IsCarriedForward)
            return Result<int>.Failure(Error.InvalidSettlementState("send emails for", "carried-forward"));

        var messages = new List<EmailMessage>();
        var skipped = 0;

        foreach (var line in reportData.Lines)
        {
            if (!IsRecipient(reportData, line))
            {
                skipped++;
                continue;
            }

            var (htmlBody, plainTextBody) = _templateService.GenerateSettlementEmail(EmailModel(reportData, line));

            messages.Add(new EmailMessage(
                line.UserEmail,
                $"Settlement Ready - {reportData.SpaceName}",
                htmlBody,
                plainTextBody));
        }

        if (messages.Count == 0)
        {
            _logger.LogInformation("Settlement emails: none to send, {Skipped} line(s) skipped", skipped);
            return Result<int>.Success(0);
        }

        var attachments = new List<EmailAttachment>();
        if (command.AttachPdf)
        {
            try
            {
                var pdfBytes = _pdfGenerator.Generate(reportData);
                var fileName = $"Settlement_{reportData.BillingPeriodName.Replace(" ", "_")}_{reportData.GeneratedAt:yyyyMMdd}.pdf";
                attachments.Add(new EmailAttachment(fileName, pdfBytes, "application/pdf"));
                _logger.LogInformation("Generated {Size}KB PDF attachment for settlement emails", pdfBytes.Length / 1024);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate PDF attachment, sending emails without it");
            }
        }

        var results = await _emailService.SendBatchAsync(messages, attachments, cancellationToken);

        var sent = results.Count(r => r.Success);
        var failed = results.Where(r => !r.Success).ToList();

        foreach (var failure in failed)
            _logger.LogError("Settlement email failed for {Recipient}: {Error}", failure.Recipient, failure.ErrorMessage);

        if (sent == 0 && failed.Count > 0)
        {
            var failedRecipients = string.Join(", ", failed.Select(f => f.Recipient));
            return Result<int>.Failure(Error.DomainError($"All emails failed. Recipients: {failedRecipients}"));
        }

        _logger.LogInformation("Settlement emails: {Sent} sent, {Failed} failed out of {Total}, {Skipped} line(s) skipped (PDF attached: {PdfAttached})",
            sent, failed.Count, messages.Count, skipped, attachments.Count > 0);

        return Result<int>.Success(sent);
    }

    /// <summary>
    /// A deliverable address, and something to read or do — consumption in this settlement, a
    /// non-zero net, or (the treasurer) at least one transfer. Legacy settlements mail every line with an address.
    /// </summary>
    private static bool IsRecipient(SettlementReportData report, SettlementLineReportData line)
    {
        if (string.IsNullOrEmpty(line.UserEmail))
            return false;
        if (!report.IsLedger)
            return true;

        return line.CoffeeGrams > 0
            || (line.MilkMl ?? 0) > 0
            || (line.Net ?? 0) != 0
            || (line.IsTreasurer && report.Lines.Any(l => !l.IsTreasurer && (l.Net ?? 0) != 0));
    }

    private static SettlementEmailModel EmailModel(SettlementReportData report, SettlementLineReportData line)
    {
        if (!report.IsLedger)
        {
            return new SettlementEmailModel(
                line.UserName, report.SpaceName, report.BillingPeriodName, report.PeriodStart, report.PeriodEnd,
                report.Currency, line.ConsumptionPercentage, line.AmountDue,
                $"Amount due: {Money(line.AmountDue, report.Currency)}",
                CurrencyDecimals: Decimals(report.Currency));
        }

        var treasurer = report.TreasurerName ?? "the treasurer";
        var net = line.Net ?? 0m;
        IReadOnlyList<string>? treasurerTransfers = null;
        string headline;

        if (line.IsTreasurer)
        {
            var others = report.Lines.Where(l => !l.IsTreasurer && (l.Net ?? 0) != 0).ToList();
            treasurerTransfers = others
                .Select(l => (l.Net < 0
                        ? $"{l.UserName} pays you {Money(-l.Net!.Value, report.Currency)}"
                        : $"you pay {l.UserName} {Money(l.Net!.Value, report.Currency)}")
                    + (IsConfirmed(l) ? " — confirmed" : ""))
                .ToList();

            // The headline counts only what is still to be collected or paid out.
            var open = others.Where(l => !IsConfirmed(l)).ToList();
            var collect = -open.Where(l => l.Net < 0).Sum(l => l.Net!.Value);
            var payOut = open.Where(l => l.Net > 0).Sum(l => l.Net!.Value);
            headline = others.Count == 0
                ? "nothing to pay"
                : open.Count == 0
                    ? "all payments through you are confirmed"
                    : string.Join(" and ", new[]
                    {
                        collect > 0 ? $"you collect {Money(collect, report.Currency)}" : null,
                        payOut > 0 ? $"you pay out {Money(payOut, report.Currency)}" : null
                    }.Where(s => s is not null)) + " as treasurer";
        }
        else if (net == 0)
        {
            headline = "nothing to pay";
        }
        else
        {
            var amount = Money(Math.Abs(net), report.Currency);
            headline = (net < 0, IsConfirmed(line)) switch
            {
                (true, false) => $"you pay {treasurer} {amount}",
                (true, true) => $"you paid {treasurer} {amount} — confirmed, thank you",
                (false, false) => $"{treasurer} pays you {amount}",
                (false, true) => $"{treasurer} paid you {amount} — confirmed"
            };
        }

        return new SettlementEmailModel(
            line.UserName, report.SpaceName, report.BillingPeriodName, report.PeriodStart, report.PeriodEnd,
            report.Currency, line.ConsumptionPercentage, line.AmountDue, headline,
            IsLedger: true,
            Net: net,
            Credit: line.Credit ?? 0m,
            CarriedIn: line.CarriedIn ?? 0m,
            Adjustments: line.Adjustments ?? 0m,
            MergeTransfer: line.MergeTransfer,
            MergedIntoName: line.MergedIntoName,
            Rounding: line.Rounding ?? 0m,
            TreasurerTransfers: treasurerTransfers,
            CurrencyDecimals: Decimals(report.Currency));
    }

    private static bool IsConfirmed(SettlementLineReportData line) =>
        line.Resolution == nameof(LineResolution.Confirmed);

    /// <summary>The amount with the currency's own decimal places (CZK 2, JPY 0, KWD 3).</summary>
    private static string Money(decimal amount, string currency) =>
        $"{amount.ToString($"N{Decimals(currency)}", CultureInfo.InvariantCulture)} {currency}";

    private static int Decimals(string currency)
    {
        try
        {
            return Domain.ValueObjects.Currency.Create(currency).DecimalPlaces;
        }
        catch (ArgumentException)
        {
            return 2;
        }
    }
}
