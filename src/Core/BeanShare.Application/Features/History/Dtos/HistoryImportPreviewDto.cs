namespace BeanShare.Application.Features.History.Dtos;

/// <summary>One historical period as the paste defines it, with its totals.</summary>
public sealed record HistoryPreviewPeriodDto(string Label, DateTime StartDate, DateTime EndDate, int Cups, decimal Cost, int PurchaseCount);

/// <summary>Cups per member over the whole paste.</summary>
public sealed record HistoryPreviewMemberDto(string Member, Guid? UserId, int Cups, bool IsNewPlaceholder);

/// <summary>A pasted row with its problems; <see cref="Block"/> is "cups" or "purchases".</summary>
public sealed record HistoryPreviewRowErrorDto(string Block, int LineNumber, string Text, IReadOnlyList<string> Errors);

/// <summary>
/// The preview of an Excel history paste. Nothing is saved. It can be committed
/// when <see cref="CanCommit"/> is true.
/// </summary>
public sealed record HistoryImportPreviewDto(
    IReadOnlyList<HistoryPreviewPeriodDto> Periods,
    IReadOnlyList<HistoryPreviewMemberDto> Members,
    IReadOnlyList<HistoryPreviewRowErrorDto> RowErrors,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> PlaceholdersToCreate,
    int TotalCups,
    decimal TotalCost,
    string Currency,
    DateTime? FirstStart,
    DateTime? LastEnd)
{
    public bool CanCommit => Errors.Count == 0 && RowErrors.Count == 0 && Periods.Count > 0;
}
