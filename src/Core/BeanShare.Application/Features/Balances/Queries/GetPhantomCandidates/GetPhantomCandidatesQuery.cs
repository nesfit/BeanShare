using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;

namespace BeanShare.Application.Features.Balances.Queries.GetPhantomCandidates;

/// <summary>
/// The rows the cutover may void: write-off shares and bag-less rows dated on or after the ledger
/// start and not in a Settled period, grouped into events. By default only write-offs and split-like rows
/// are listed; <paramref name="IncludeAllBagLess"/> adds every other bag-less row.
/// </summary>
[RequireSpaceAdmin("SpaceId")]
public sealed record GetPhantomCandidatesQuery(Guid SpaceId, bool IncludeAllBagLess = false)
    : IAuthorize, IQuery<Result<PhantomCandidatesDto>>;

public sealed record PhantomCandidatesDto(
    DateTime? LedgerStartDate,
    bool IncludesAllBagLess,
    IReadOnlyList<PhantomEventDto> Events);

/// <summary>
/// Rows of one product logged in the same minute, with the same preset name and bag.
/// <see cref="LooksLikeSplit"/>: bag-less rows without a preset id but with a preset name, as the old
/// product-level split wrote them.
/// </summary>
public sealed record PhantomEventDto(
    DateTime At,
    string Product,
    Guid? BagId,
    string? BagLabel,
    string? PresetName,
    bool IsWriteOff,
    bool LooksLikeSplit,
    string? PeriodName,
    string PeriodState,
    bool CanVoid,
    decimal TotalGrams,
    decimal TotalCost,
    IReadOnlyList<PhantomRowDto> Rows);

public sealed record PhantomRowDto(Guid ConsumptionId, Guid UserId, string UserName, decimal Grams, decimal? Cost);
