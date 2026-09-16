using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;

namespace BeanShare.Application.Features.CoffeeStock.Queries.GetSpaceLedger;

/// <summary>
/// A unified, filterable/sortable/paged ledger of stock movements for a space: every purchase
/// (+ inflow) and every cup (− outflow), each tied to its bag, with a per-product running balance.
/// </summary>
[RequireSpaceMember("SpaceId")]
public sealed record GetSpaceLedgerQuery(
    Guid SpaceId,
    string? Kind = null,            // "purchase" | "cup" | "discard" | null (all)
    string? Product = null,         // substring match on product display name
    Guid? MemberUserId = null,      // buyer (purchase) or drinker (cup)
    DateTime? StartDate = null,
    DateTime? EndDate = null,
    string SortBy = "ts",           // ts | kind | product | delta | balance | who | cost
    bool SortDescending = true,
    int Page = 1,
    int PageSize = 25
) : IAuthorize, IQuery<Result<LedgerPageDto>>;

public sealed record LedgerEntryDto(
    DateTime Timestamp,
    string Kind,                    // "Purchase" | "Cup" | "Discard"
    string ProductDisplayName,
    string ProductType,
    string BagLabel,
    decimal DeltaGrams,             // + for a purchase, − for a cup
    decimal RunningBalanceGrams,    // this product's balance after the movement
    Guid UserId,
    string UserName,
    decimal? CostAmount,
    string? CostCurrency,
    string? Note,
    Guid? ConsumptionEntryId = null,
    bool CanEdit = false,
    string ProductName = "",
    string ProductBrand = "");

public sealed record LedgerTotalsDto(
    decimal PurchasedGrams,
    decimal PurchasedCost,
    decimal ConsumedGrams,
    decimal ConsumedCost,
    decimal NetGrams,
    int PurchaseCount,
    int CupCount,
    string Currency,
    decimal DiscardedGrams = 0m,      // thrown away unbilled by an admin
    decimal DiscardedCost = 0m,       // its sunk value at the bags' own €/g
    int DiscardCount = 0);

public sealed record LedgerPageDto(
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<LedgerEntryDto> Entries,
    LedgerTotalsDto Totals);
