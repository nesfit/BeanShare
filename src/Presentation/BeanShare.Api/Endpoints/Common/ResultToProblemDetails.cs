using System.Net;
using BeanShare.Application.Common;
using FastEndpoints;

namespace BeanShare.Api.Endpoints.Common;

/// <summary>
/// Helper for translating Result/Result&lt;T&gt; to HTTP responses with correct status codes.
/// Maps domain error codes to appropriate HTTP status codes following REST conventions.
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Adds errors from a Result to FastEndpoints error collection and sends error response.
    /// Automatically maps error codes to appropriate HTTP status codes.
    /// </summary>
    public static async Task SendResultErrorsAsync<TRequest, TResponse>(
        this Endpoint<TRequest, TResponse> endpoint,
        Result result,
        CancellationToken ct = default)
        where TRequest : notnull
    {
        if (!result.IsFailure)
            return;

        foreach (var error in result.Errors)
        {
            endpoint.AddError(error.Code, error.Message);
        }

        var statusCode = GetStatusCode(result.Errors);
        await endpoint.HttpContext.Response.SendErrorsAsync(endpoint.ValidationFailures, statusCode, cancellation: ct);
    }

    /// <summary>
    /// Adds errors from a Result to FastEndpoints error collection and sends error response.
    /// Automatically maps error codes to appropriate HTTP status codes.
    /// </summary>
    public static async Task SendResultErrorsAsync<TRequest, TResponse, TValue>(
        this Endpoint<TRequest, TResponse> endpoint,
        Result<TValue> result,
        CancellationToken ct = default)
        where TRequest : notnull
    {
        if (!result.IsFailure)
            return;

        foreach (var error in result.Errors)
        {
            endpoint.AddError(error.Code, error.Message);
        }

        var statusCode = GetStatusCode(result.Errors);
        await endpoint.HttpContext.Response.SendErrorsAsync(endpoint.ValidationFailures, statusCode, cancellation: ct);
    }

    /// <summary>
    /// For endpoints that shape their own failures: sends a CONCURRENCY_CONFLICT failure as 409 with the
    /// error codes in the body, the way <c>SendResultErrorsAsync</c> does, and returns true. Any other
    /// result (success included) is left to the caller.
    /// </summary>
    public static async Task<bool> TrySendConflictAsync<TRequest, TResponse>(
        this Endpoint<TRequest, TResponse> endpoint,
        IReadOnlyList<Error> errors,
        CancellationToken ct = default)
        where TRequest : notnull
    {
        if (!IsConcurrencyConflict(errors))
            return false;

        foreach (var error in errors)
        {
            endpoint.AddError(error.Code, error.Message);
        }

        await endpoint.HttpContext.Response.SendErrorsAsync(endpoint.ValidationFailures, (int)HttpStatusCode.Conflict, cancellation: ct);
        return true;
    }

    /// <summary>A retryable race: the data changed while the request ran.</summary>
    public static bool IsConcurrencyConflict(IReadOnlyList<Error> errors) =>
        errors.Any(e => e.Code == Error.Codes.ConcurrencyConflict);

    /// <summary>
    /// Gets the appropriate HTTP status code for a collection of errors.
    /// Uses the first error to determine status code.
    /// </summary>
    public static int GetStatusCode(IReadOnlyList<Error> errors)
    {
        if (errors.Count == 0)
            return (int)HttpStatusCode.BadRequest;

        return GetStatusCode(errors[0]);
    }

    /// <summary>
    /// Maps an error code to the appropriate HTTP status code.
    /// Error codes use UPPER_SNAKE_CASE as defined in Error.Codes.
    /// </summary>
    public static int GetStatusCode(Error error)
    {
        return error.Code switch
        {
            // 401 Unauthorized
            Error.Codes.Unauthenticated => (int)HttpStatusCode.Unauthorized,
            Error.Codes.Unauthorized => (int)HttpStatusCode.Unauthorized,

            // 403 Forbidden
            Error.Codes.NotMember => (int)HttpStatusCode.Forbidden,
            Error.Codes.NotAdmin => (int)HttpStatusCode.Forbidden,
            Error.Codes.Forbidden => (int)HttpStatusCode.Forbidden,
            Error.Codes.InsufficientPrivileges => (int)HttpStatusCode.Forbidden,
            Error.Codes.EmailNotVerified => (int)HttpStatusCode.Forbidden,
            Error.Codes.SyncFailed => (int)HttpStatusCode.Forbidden,

            // 404 Not Found
            Error.Codes.SpaceNotFound => (int)HttpStatusCode.NotFound,
            Error.Codes.StockNotFound => (int)HttpStatusCode.NotFound,
            Error.Codes.StockLevelNotFound => (int)HttpStatusCode.NotFound,
            Error.Codes.ProductNotFoundInStock => (int)HttpStatusCode.NotFound,
            Error.Codes.NotFound => (int)HttpStatusCode.NotFound,
            Error.Codes.UserNotFound => (int)HttpStatusCode.NotFound,
            Error.Codes.MemberNotFound => (int)HttpStatusCode.NotFound,
            Error.Codes.BillingPeriodNotFound => (int)HttpStatusCode.NotFound,
            Error.Codes.SettlementNotFound => (int)HttpStatusCode.NotFound,
            Error.Codes.SettlementLineNotFound => (int)HttpStatusCode.NotFound,
            Error.Codes.NotificationNotFound => (int)HttpStatusCode.NotFound,

            // 409 Conflict
            Error.Codes.InsufficientStock => (int)HttpStatusCode.Conflict,
            Error.Codes.CurrencyConflict => (int)HttpStatusCode.Conflict,
            Error.Codes.AlreadyMember => (int)HttpStatusCode.Conflict,
            Error.Codes.BillingPeriodOverlap => (int)HttpStatusCode.Conflict,
            Error.Codes.SettlementAlreadyExists => (int)HttpStatusCode.Conflict,
            Error.Codes.PaymentAlreadyConfirmed => (int)HttpStatusCode.Conflict,
            Error.Codes.LastAdminProtection => (int)HttpStatusCode.Conflict,
            Error.Codes.CannotRemoveLastAdmin => (int)HttpStatusCode.Conflict,
            Error.Codes.UnpaidSettlements => (int)HttpStatusCode.Conflict,
            Error.Codes.ConcurrencyConflict => (int)HttpStatusCode.Conflict,
            Error.Codes.PlaceholderLinkPending => (int)HttpStatusCode.Conflict,
            Error.Codes.EmailInUse => (int)HttpStatusCode.Conflict,

            // 422 Unprocessable Entity (domain logic errors)
            Error.Codes.InvalidBillingPeriodState => (int)HttpStatusCode.UnprocessableEntity,
            Error.Codes.InvalidSettlementState => (int)HttpStatusCode.UnprocessableEntity,
            Error.Codes.NoConsumptionsInPeriod => (int)HttpStatusCode.UnprocessableEntity,
            Error.Codes.CannotPromoteMember => (int)HttpStatusCode.UnprocessableEntity,
            Error.Codes.CannotDemoteMember => (int)HttpStatusCode.UnprocessableEntity,
            Error.Codes.NoRemainingStock => (int)HttpStatusCode.UnprocessableEntity,
            Error.Codes.DomainError => (int)HttpStatusCode.UnprocessableEntity,
            Error.Codes.ConfirmationFailed => (int)HttpStatusCode.UnprocessableEntity,

            // 400 Bad Request (validation / input errors)
            Error.Codes.ValidationError => (int)HttpStatusCode.BadRequest,
            Error.Codes.InvalidRequest => (int)HttpStatusCode.BadRequest,
            Error.Codes.InvalidSpaceName => (int)HttpStatusCode.BadRequest,
            Error.Codes.InviteCodeInvalid => (int)HttpStatusCode.BadRequest,
            Error.Codes.InvalidCoffeeType => (int)HttpStatusCode.BadRequest,
            Error.Codes.InvalidConsumptionTime => (int)HttpStatusCode.BadRequest,
            Error.Codes.InvalidProductType => (int)HttpStatusCode.BadRequest,
            Error.Codes.InvalidCurrency => (int)HttpStatusCode.BadRequest,
            Error.Codes.CsvImportFailed => (int)HttpStatusCode.BadRequest,
            Error.Codes.CsvImportInvalidFormat => (int)HttpStatusCode.BadRequest,
            Error.Codes.EntryLocked => (int)HttpStatusCode.BadRequest,
            Error.Codes.LineCarried => (int)HttpStatusCode.BadRequest,
            Error.Codes.LedgerStartLocked => (int)HttpStatusCode.BadRequest,
            Error.Codes.SettlementOrder => (int)HttpStatusCode.BadRequest,
            Error.Codes.TreasurerRequired => (int)HttpStatusCode.BadRequest,
            Error.Codes.BeforeLedgerStart => (int)HttpStatusCode.BadRequest,
            Error.Codes.MemberHasBalance => (int)HttpStatusCode.BadRequest,
            Error.Codes.PeriodNotDeletable => (int)HttpStatusCode.BadRequest,
            Error.Codes.LineMerged => (int)HttpStatusCode.BadRequest,
            Error.Codes.MemberMerged => (int)HttpStatusCode.BadRequest,
            Error.Codes.NotPlaceholder => (int)HttpStatusCode.BadRequest,
            Error.Codes.HistoryImportInvalid => (int)HttpStatusCode.BadRequest,
            Error.Codes.BagPaidInExcel => (int)HttpStatusCode.BadRequest,
            Error.Codes.NotVoidable => (int)HttpStatusCode.BadRequest,

            // 500 Internal Server Error
            Error.Codes.SystemError => (int)HttpStatusCode.InternalServerError,

            _ => (int)HttpStatusCode.BadRequest
        };
    }
}