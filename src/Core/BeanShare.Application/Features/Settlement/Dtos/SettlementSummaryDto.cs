namespace BeanShare.Application.Features.Settlement.Dtos;
public sealed record SettlementSummaryDto(
    Guid Id,
    string BillingPeriodName,
    DateTime GeneratedAt,
    decimal TotalAmount,
    string Currency,
    string Status,
    int ConfirmedLinesCount,
    int TotalLinesCount,
    int UserCount,
    decimal YourShare,
    decimal? YourNet = null,
    string? Model = null,
    bool IsCarriedForward = false
);
