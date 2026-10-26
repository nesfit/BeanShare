namespace BeanShare.Domain.Exceptions;

/// <summary>
/// A money-ledger rule was violated. <see cref="Code"/> is the stable error code surfaced to clients.
/// </summary>
public sealed class LedgerDomainException : DomainException
{
    public const string EntryLocked = "ENTRY_LOCKED";
    public const string LedgerStartLocked = "LEDGER_START_LOCKED";
    public const string LineCarried = "LINE_CARRIED";
    public const string ConfirmationFailed = "CONFIRMATION_FAILED";

    public string Code { get; }

    public LedgerDomainException(string code, string message) : base(message)
    {
        Code = code;
    }
}
