namespace BeanShare.Application.Common;

/// <summary>
/// A save lost a race: a concurrency token did not match, or a guarding unique index was hit.
/// <see cref="EntityTypes"/> holds the root (aggregate) CLR types of the conflicting entries.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(
        IReadOnlyCollection<Type> entityTypes,
        Exception innerException,
        string errorCode = Error.Codes.ConcurrencyConflict)
        : base("The data changed while the operation ran", innerException)
    {
        EntityTypes = entityTypes;
        ErrorCode = errorCode;
    }

    public IReadOnlyCollection<Type> EntityTypes { get; }

    public string ErrorCode { get; }

    public Error ToError() => ErrorCode == Error.Codes.EmailInUse
        ? Error.EmailInUse()
        : Error.ConcurrencyConflict();
}
