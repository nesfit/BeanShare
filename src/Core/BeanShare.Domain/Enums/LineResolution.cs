namespace BeanShare.Domain.Enums;

/// <summary>Resolution of a ledger settlement line.</summary>
public enum LineResolution
{
    Open = 0,
    Confirmed = 1,

    /// <summary>Not confirmed before the next settlement; its net was carried into that settlement.</summary>
    Carried = 2
}
