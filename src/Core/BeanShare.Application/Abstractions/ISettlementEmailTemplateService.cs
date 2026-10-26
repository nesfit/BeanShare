namespace BeanShare.Application.Abstractions;

/// <summary>
/// Service for generating settlement email templates.
/// </summary>
public interface ISettlementEmailTemplateService
{
    /// <summary>
    /// Generates HTML and plain text email bodies for a settlement notification.
    /// </summary>
    (string HtmlBody, string PlainTextBody) GenerateSettlementEmail(SettlementEmailModel model, string? appUrl = null);
}

/// <summary>
/// What one recipient's settlement email says. <see cref="Headline"/> is already worded for the line's state
/// ("you pay {T} X", "you paid {T} X — confirmed, thank you", "nothing to pay"; "Amount due X" for legacy).
/// The breakdown fields are used for ledger settlements only.
/// </summary>
/// <param name="TreasurerTransfers">For the treasurer: one sentence per payment they collect or make.</param>
/// <param name="CurrencyDecimals">How many decimal places amounts in <see cref="Currency"/> are shown with.</param>
public sealed record SettlementEmailModel(
    string UserName,
    string SpaceName,
    string BillingPeriodName,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    string Currency,
    decimal ConsumptionPercentage,
    decimal Cost,
    string Headline,
    bool IsLedger = false,
    decimal Net = 0m,
    decimal Credit = 0m,
    decimal CarriedIn = 0m,
    decimal Adjustments = 0m,
    decimal MergeTransfer = 0m,
    string? MergedIntoName = null,
    decimal Rounding = 0m,
    IReadOnlyList<string>? TreasurerTransfers = null,
    int CurrencyDecimals = 2);
