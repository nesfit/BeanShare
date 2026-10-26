namespace BeanShare.Application.Features.Settlement.Dtos;

/// <summary>
/// Data model for settlement report generation.
/// </summary>
public sealed record SettlementReportData(
    // Header
    string SpaceName,
    string BillingPeriodName,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    DateTime GeneratedAt,
    string GeneratedByName,

    // Summary
    decimal TotalAmount,
    string Currency,
    int TotalParticipants,
    decimal TotalCoffeeGrams,
    decimal TotalMilkMl,

    // Lines
    IReadOnlyList<SettlementLineReportData> Lines,

    // Ledger settlements
    bool IsLedger = false,
    string? TreasurerName = null,
    bool IsCarriedForward = false
);

/// <summary>
/// Individual line item data for settlement report.
/// </summary>
public sealed record SettlementLineReportData(
    string UserName,
    string UserEmail,
    decimal CoffeeGrams,
    decimal? MilkMl,
    decimal ConsumptionPercentage,
    decimal AmountDue,
    bool IsPaid,
    string? ConfirmedByName,
    DateTime? ConfirmedAt,
    decimal? Credit = null,
    decimal? CarriedIn = null,
    decimal? Adjustments = null,
    decimal? Rounding = null,
    decimal? Net = null,
    string? Resolution = null,
    string? TransferText = null
);
