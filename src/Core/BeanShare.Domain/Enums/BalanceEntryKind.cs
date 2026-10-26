namespace BeanShare.Domain.Enums;

/// <summary>Why a balance entry exists: the position carried in from before the ledger, or a later fix.</summary>
public enum BalanceEntryKind
{
    Opening = 0,
    Correction = 1
}
