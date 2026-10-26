namespace BeanShare.Application.Features.History.Dtos;

/// <summary>
/// The History view (DS-16): imported periods with their cups and purchases, per-member all-time cups
/// (history plus live cups after the last historical end) and purchases per year. Statistics only.
/// </summary>
public sealed record SpaceHistoryDto(
    Guid SpaceId,
    string Currency,
    bool IsAdmin,
    DateTime? HistoryEnd,
    IReadOnlyList<HistoryImportSummaryDto> Imports,
    IReadOnlyList<HistoricalPeriodDto> Periods,
    IReadOnlyList<HistoryMemberCupsDto> MemberCups,
    IReadOnlyList<HistoryYearDto> Years);

public sealed record HistoryImportSummaryDto(
    Guid Id,
    DateTime ImportedAt,
    string ImportedByName,
    string Note,
    int PeriodCount,
    int Cups,
    decimal Cost);

public sealed record HistoricalPeriodDto(
    Guid Id,
    Guid ImportId,
    string Name,
    DateTime StartDate,
    DateTime EndDate,
    IReadOnlyList<HistoricalCupsDto> Cups,
    IReadOnlyList<HistoricalPurchaseDto> Purchases)
{
    public int TotalCups => Cups.Sum(c => c.Cups);
    public decimal TotalCost => Purchases.Sum(p => p.Cost);
}

public sealed record HistoricalCupsDto(Guid UserId, string UserName, int Cups);

public sealed record HistoricalPurchaseDto(
    DateTime? PurchasedAt,
    Guid? PaidByUserId,
    string? PaidByName,
    string Description,
    decimal? Grams,
    decimal Cost);

/// <param name="LiveCups">Cups logged in BeanShare after the last historical period.</param>
public sealed record HistoryMemberCupsDto(Guid UserId, string UserName, int HistoricalCups, int LiveCups)
{
    public int TotalCups => HistoricalCups + LiveCups;
}

/// <summary>Coffee bought per year: the history's purchases plus live purchases after the history.</summary>
public sealed record HistoryYearDto(int Year, decimal Cost, decimal Grams, int PurchaseCount);
