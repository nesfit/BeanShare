using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;

namespace BeanShare.Application.Features.CoffeeStock.Commands;

/// <summary>
/// Reverses an accidental <see cref="MarkBagEmptyCommand"/>: deletes the phantom-fill cups it billed
/// to the bag's drinkers and puts the written-off grams back into the bag (which returns to Sealed if
/// nothing had ever been poured from it, else to Open). Allowed only within
/// <see cref="Domain.Aggregates.CoffeeStock.Purchase.WriteOffUndoWindow"/> of the emptying and only
/// while none of those cups sits in a closed or settled billing period.
/// </summary>
public sealed record UndoBagEmptyCommand(Guid SpaceId, Guid LotId) : ICommand<Result<UndoBagEmptyResult>>;

public sealed record UndoBagEmptyResult(
    Guid LotId,
    string ProductDisplayName,
    string BagLabel,
    decimal RestoredGrams,
    int RemovedEntries,
    string NewStatus);
