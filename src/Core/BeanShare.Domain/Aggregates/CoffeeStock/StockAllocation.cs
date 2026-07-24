using BeanShare.Domain.ValueObjects;

namespace BeanShare.Domain.Aggregates.CoffeeStock;

/// <summary>
/// The result of consuming stock: which single bag (lot) a cup was drawn from, the cost
/// stamped from that bag's price, and the product's remaining stock after the draw.
/// Returned by <see cref="CoffeeStock.ConsumeStock"/> so callers can stamp the cost onto
/// the <c>ConsumptionEntry</c>. One cup always maps to exactly one bag (no cross-lot spill).
/// </summary>
public sealed record StockAllocation(
    Guid LotId,
    string LotLabel,
    Weight Grams,
    decimal UnitCostPerGram,
    Money Cost,
    Weight ProductRemaining);
