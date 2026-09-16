using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;

namespace BeanShare.Application.Features.CoffeeStock.Commands;

/// <summary>
/// Throws away whatever is left in a bag <b>without billing anyone</b>: the coffee is gone (stale,
/// spilled, or a stock record that never matched reality) and the space eats the cost. The bag
/// becomes Empty and the discarded grams show in the ledger as an unattributed outflow. The
/// counterpart of <see cref="MarkBagEmptyCommand"/>, which bills the remainder to the drinkers.
/// Undoable within 24 h via <see cref="UndoBagEmptyCommand"/>.
/// </summary>
public sealed record DiscardBagCommand(Guid SpaceId, Guid LotId) : ICommand<Result<DiscardBagResult>>;

public sealed record DiscardBagResult(
    Guid LotId,
    string ProductDisplayName,
    string BagLabel,
    decimal DiscardedGrams,
    decimal SunkCostAmount,
    string Currency);
