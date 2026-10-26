namespace BeanShare.Contracts.Billing;

public record BillingPeriodDto(
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
    string Currency,
    bool IsHistorical = false
)
{
    public string FormattedDateRange { get; init; } = string.Empty;
    public string FormattedDateRangeLong { get; init; } = string.Empty;
};

public record BillingPeriodSummaryDto(
    Guid Id,
    string Name,
    DateTime StartDate,
    DateTime EndDate,
    string State,
    int DaysRemaining,
    int ConsumptionCount,
    decimal TotalCoffeeGrams,
    bool IsHistorical = false
)
{
    public string FormattedDateRange { get; init; } = string.Empty;
};

public record CreateBillingPeriodDto
{
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}
