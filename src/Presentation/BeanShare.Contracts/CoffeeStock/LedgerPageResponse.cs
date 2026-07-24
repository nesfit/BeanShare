namespace BeanShare.Contracts.CoffeeStock;

/// <summary>A page of ledger entries plus the totals for the full filtered set. Mirrors LedgerPageDto.</summary>
public sealed record LedgerPageResponse
{
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required IReadOnlyList<LedgerEntryResponse> Entries { get; init; }
    public required LedgerTotalsResponse Totals { get; init; }
}
