namespace BeanShare.Domain.Enums;

/// <summary>
/// How a settlement was computed. A null model is a legacy settlement (gross cost only, never carried).
/// </summary>
public enum SettlementModel
{
    /// <summary>Running-balance settlement: snapshot lines with credit, carry and entries, net through the treasurer.</summary>
    Ledger = 1
}
