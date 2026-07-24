namespace BeanShare.Domain.Enums;

/// <summary>
/// Lifecycle of a single physical bag of coffee (a purchase lot).
/// </summary>
public enum LotStatus
{
    /// <summary>Bought but not yet opened; no consumption drawn from it.</summary>
    Sealed = 1,

    /// <summary>Currently being poured from. At most one open bag per product is expected.</summary>
    Open = 2,

    /// <summary>Fully consumed (or written off); no stock remains.</summary>
    Empty = 3
}
