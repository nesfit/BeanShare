using BeanShare.Domain.Aggregates.BillingPeriod;

namespace BeanShare.Application.Features.Billing.Dtos;
public sealed record BillingPeriodSummaryDto(
    Guid Id,
    string Name,
    DateTime StartDate,
    DateTime EndDate,
    string State,
    int DaysRemaining,
    int ConsumptionCount,
    decimal TotalCoffeeGrams,
    bool IsHistorical = false,
    Guid? HistoryImportId = null
)
{
    public bool IsOpenEnded => EndDate >= BillingPeriod.OpenEndedSentinel.Date;

    public string FormattedDateRange => IsOpenEnded
        ? $"{StartDate:MMM dd, yyyy} — Open-ended"
        : $"{StartDate:MMM dd, yyyy} — {EndDate:MMM dd, yyyy}";
};
