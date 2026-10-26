using System.Net;
using BeanShare.Application.Common;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;

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

        await SendCodedErrorsAsync(endpoint, result.Errors, GetStatusCode(result.Errors), ct);
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

        await SendCodedErrorsAsync(endpoint, result.Errors, GetStatusCode(result.Errors), ct);
    }

    /// <summary>
    /// For endpoints that shape their own failures: sends an access failure (a code that maps to 401 or 403, such as
    /// UNAUTHENTICATED or NOT_ADMIN) or a CONCURRENCY_CONFLICT (409) with that status and the error codes in the
    /// body, the way <c>SendResultErrorsAsync</c> does, and returns true. Every other result, success and business
    /// failures included, is left to the caller and keeps the endpoint's own shape.
    /// </summary>
    public static async Task<bool> TrySendAccessOrConflictAsync<TRequest, TResponse>(
        this Endpoint<TRequest, TResponse> endpoint,
        IReadOnlyList<Error> errors,
        CancellationToken ct = default)
        where TRequest : notnull
    {
        var status = IsConcurrencyConflict(errors) ? (int)HttpStatusCode.Conflict : GetStatusCode(errors);
        if (status is not ((int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden) && !IsConcurrencyConflict(errors))
            return false;

        await SendCodedErrorsAsync(endpoint, errors, status, ct);
        return true;
    }

    /// <summary>
    /// For endpoints that keep their own status for a failure: sends it with <paramref name="statusCode"/> and the
    /// error codes in the body, in the same shape as <c>SendResultErrorsAsync</c>.
    /// </summary>
    public static async Task SendErrorsWithCodesAsync<TRequest, TResponse>(
        this Endpoint<TRequest, TResponse> endpoint,
        IReadOnlyList<Error> errors,
        int statusCode,
        CancellationToken ct = default)
        where TRequest : notnull
    {
        await SendCodedErrorsAsync(endpoint, errors, statusCode, ct);
    }

    private const string ProblemJson = "application/problem+json; charset=utf-8";

    /// <summary>
    /// Sends <paramref name="errors"/> as FastEndpoints' problem details with <paramref name="statusCode"/>: each code in
    /// <c>Errors[].Reason</c> and the first error's message in <c>Detail</c>. A 401 also carries the Bearer challenge
    /// (RFC 9110 §15.5.2), as the authentication middleware's own 401 does.
    /// </summary>
    private static async Task SendCodedErrorsAsync<TRequest, TResponse>(
        Endpoint<TRequest, TResponse> endpoint,
        IReadOnlyList<Error> errors,
        int statusCode,
        CancellationToken ct)
        where TRequest : notnull
    {
        foreach (var error in errors)
        {
            endpoint.AddError(error.Code, error.Message);
        }

        var http = endpoint.HttpContext;
        if (statusCode == (int)HttpStatusCode.Unauthorized)
            http.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;

        var problem = new ProblemDetails(endpoint.ValidationFailures, http.Request.Path, http.TraceIdentifier, statusCode)
        {
            Detail = errors.Count > 0 ? errors[0].Message : null
        };
        // What FastEndpoints' SendErrorsAsync does with UseProblemDetails(), with this body: its JSON options and the
        // problem-details content type.
        http.MarkResponseStart();
        http.Response.StatusCode = statusCode;
        await http.Response.WriteAsJsonAsync(problem, new Config().Serializer.Options, ProblemJson, ct);
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