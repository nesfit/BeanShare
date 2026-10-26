using BeanShare.Domain.Aggregates.BillingPeriod;

namespace BeanShare.Application.Features.Billing.Dtos;
public sealed record BillingPeriodDto(
    Guid Id,
    Guid SpaceId,
    string Name,
    DateTime StartDate,
    DateTime EndDate,
    string State,
    DateTime CreatedAt,
    Guid CreatedBy,
    DateTime? OpenedAt,
    Guid? OpenedBy,
    DateTime? ClosedAt,
    Guid? ClosedBy,
    DateTime? SettledAt,
    Guid? SettledBy,
    int ConsumptionCount,
    decimal TotalCoffeeGrams,
    decimal? EstimatedCost,
    string? Currency,
    bool IsHistorical = false,
    Guid? HistoryImportId = null
)
{
    public bool IsOpenEnded => EndDate >= BillingPeriod.OpenEndedSentinel.Date;

    public string FormattedDateRange => IsOpenEnded
        ? $"{StartDate:MMM dd, yyyy} — Open-ended"
        : $"{StartDate:MMM dd, yyyy} — {EndDate:MMM dd, yyyy}";

    public string FormattedDateRangeLong => IsOpenEnded
        ? $"{StartDate:MMMM dd, yyyy} — Open-ended"
        : $"{StartDate:MMMM dd, yyyy} — {EndDate:MMMM dd, yyyy}";
};
