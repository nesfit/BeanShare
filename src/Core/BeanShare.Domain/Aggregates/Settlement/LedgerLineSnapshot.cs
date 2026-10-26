using BeanShare.Domain.Common;

namespace BeanShare.Domain.Aggregates.Settlement;

/// <summary>
/// The amounts of one member's ledger settlement line, all rounded to the currency's decimals.
/// Net = Credit − Cost + CarriedIn + Adjustments + Rounding; positive means the member is owed.
/// </summary>
public sealed record LedgerLineSnapshot(
    UserId UserId,
    decimal CoffeeGrams,
    decimal? MilkMl,
    decimal Cost,
    decimal Credit,
    decimal CarriedIn,
    decimal Adjustments,
    decimal Rounding)
{
    public decimal Net => Credit - Cost + CarriedIn + Adjustments + Rounding;
}
