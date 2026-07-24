namespace BeanShare.Contracts.CoffeeStock;

/// <summary>
/// Query for GET /api/spaces/{spaceId}/stock/ledger. SpaceId comes from the route; the rest are
/// optional query-string filters/sort/paging that mirror GetSpaceLedgerQuery.
/// </summary>
public sealed record GetSpaceLedgerRequest
{
    public Guid SpaceId { get; init; }
    public string? Kind { get; init; }            // "purchase" | "cup" | null (all)
    public string? Product { get; init; }         // substring match on product display name
    public Guid? MemberUserId { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public string SortBy { get; init; } = "ts";   // ts | kind | product | delta | balance | who | cost
    public bool SortDescending { get; init; } = true;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}
