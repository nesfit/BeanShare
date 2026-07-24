using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;

namespace BeanShare.Application.Features.CoffeeStock.Commands;

/// <summary>
/// Empties a bag whose physical coffee is gone. The untracked remainder is <b>always billed</b>
/// (phantom-fill): it is distributed among that bag's drinkers — pro-rata by their logged grams from
/// it, falling back to the product's / space's consumers, and finally an equal split among members —
/// as real consumption entries priced at the bag's own €/g. Replaces the old SplitRemainingStock.
/// </summary>
public sealed record MarkBagEmptyCommand(
    Guid SpaceId,
    Guid LotId,
    string Reason = "Bag emptied"
) : ICommand<Result<MarkBagEmptyResult>>;

public sealed record MarkBagEmptyResult(
    Guid LotId,
    string ProductDisplayName,
    string BagLabel,
    decimal DistributedGrams,
    int MemberCount);
