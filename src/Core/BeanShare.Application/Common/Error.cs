using static BeanShare.Application.Common.Error;

namespace BeanShare.Application.Common;

public readonly record struct Error(string Code, string Message)
{
    public static class Codes
    {
        public const string SpaceNotFound = "SPACE_NOT_FOUND";
        public const string InviteCodeInvalid = "INVITE_CODE_INVALID";
        public const string AlreadyMember = "ALREADY_MEMBER";
        public const string InsufficientPrivileges = "INSUFFICIENT_PRIVILEGES";
        public const string LastAdminProtection = "LAST_ADMIN_PROTECTION";
        public const string InvalidSpaceName = "INVALID_SPACE_NAME";
        public const string ValidationError = "VALIDATION_ERROR";
        public const string SystemError = "SYSTEM_ERROR";
        public const string Unauthorized = "UNAUTHORIZED";
        public const string CannotRemoveLastAdmin = "CANNOT_REMOVE_LAST_ADMIN";
        public const string MemberNotFound = "MEMBER_NOT_FOUND";
        public const string CannotPromoteMember = "CANNOT_PROMOTE_MEMBER";
        public const string CannotDemoteMember = "CANNOT_DEMOTE_MEMBER";
        public const string BillingPeriodOverlap = "BILLING_PERIOD_OVERLAP";
        public const string BillingPeriodNotFound = "BILLING_PERIOD_NOT_FOUND";
        public const string InvalidBillingPeriodState = "INVALID_BILLING_PERIOD_STATE";
        public const string SettlementAlreadyExists = "SETTLEMENT_ALREADY_EXISTS";
        public const string NoConsumptionsInPeriod = "NO_CONSUMPTIONS_IN_PERIOD";
        public const string NotFound = "NOT_FOUND";
        public const string Forbidden = "FORBIDDEN";
        public const string SettlementNotFound = "SETTLEMENT_NOT_FOUND";
        public const string SettlementLineNotFound = "SETTLEMENT_LINE_NOT_FOUND";
        public const string PaymentAlreadyConfirmed = "PAYMENT_ALREADY_CONFIRMED";
        public const string InvalidSettlementState = "INVALID_SETTLEMENT_STATE";
        public const string UserNotFound = "USER_NOT_FOUND";
        public const string NotificationNotFound = "NOTIFICATION_NOT_FOUND";
        public const string UnpaidSettlements = "UNPAID_SETTLEMENTS";

        // Authentication errors
        public const string Unauthenticated = "UNAUTHENTICATED";
        public const string InvalidRequest = "INVALID_REQUEST";
        public const string NotMember = "NOT_MEMBER";
        public const string NotAdmin = "NOT_ADMIN";

        // Domain errors
        public const string DomainError = "DOMAIN_ERROR";
        public const string ConfirmationFailed = "CONFIRMATION_FAILED";

        // Consumption errors
        public const string InvalidCoffeeType = "INVALID_COFFEE_TYPE";
        public const string InvalidConsumptionTime = "INVALID_CONSUMPTION_TIME";

        // Stock errors
        public const string StockNotFound = "STOCK_NOT_FOUND";
        public const string StockLevelNotFound = "STOCK_LEVEL_NOT_FOUND";
        public const string NoRemainingStock = "NO_REMAINING_STOCK";
        public const string ProductNotFoundInStock = "PRODUCT_NOT_FOUND_IN_STOCK";
        public const string InsufficientStock = "INSUFFICIENT_STOCK";
        public const string InvalidProductType = "INVALID_PRODUCT_TYPE";
        public const string CurrencyConflict = "CURRENCY_CONFLICT";
        public const string CannotUndoBagEmpty = "CANNOT_UNDO_BAG_EMPTY";

        // Currency errors
        public const string InvalidCurrency = "INVALID_CURRENCY";

        // CSV import errors
        public const string CsvImportFailed = "CSV_IMPORT_FAILED";
        public const string CsvImportInvalidFormat = "CSV_IMPORT_INVALID_FORMAT";
    }

    public static Error SpaceNotFound(Guid spaceId) =>
        new(Codes.SpaceNotFound, $"Space {spaceId} not found");

    public static Error InviteCodeNotFound(string inviteCode) =>
        new(Codes.InviteCodeInvalid, $"Invalid or expired invite code");

    public static Error AlreadySpaceMember(Guid spaceId, Guid userId) =>
        new(Codes.AlreadyMember, $"Already a member of this space");

    public static Error InsufficientSpacePrivileges(string action) =>
        new(Codes.InsufficientPrivileges, $"Not authorized to {action}");

    public static Error LastAdminProtection() =>
        new(Codes.LastAdminProtection, "Cannot remove or demote the last admin of a coffee space");

    public static Error InvalidSpaceName(string? name) =>
        new(Codes.InvalidSpaceName, $"Coffee space name '{name}' is invalid or empty");

    public static Error ValidationFailure(string field, string message) =>
        new(Codes.ValidationError, $"{field}: {message}");

    public static Error SystemFailure(string operation) =>
        new(Codes.SystemError, $"System error occurred during {operation}");

    public static Error Unauthorized() =>
        new(Codes.Unauthorized, "User is not authenticated");

    public static Error CannotRemoveLastAdmin() =>
        new(Codes.CannotRemoveLastAdmin, "Cannot remove the last admin from a coffee space");

    public static Error MemberNotFound(Guid userId, Guid spaceId) =>
        new(Codes.MemberNotFound, $"User {userId} is not a member of coffee space {spaceId}");

    public static Error CannotPromoteMember(string reason) =>
        new(Codes.CannotPromoteMember, $"Cannot promote member: {reason}");

    public static Error CannotDemoteMember(string reason) =>
        new(Codes.CannotDemoteMember, $"Cannot demote member: {reason}");

    public static Error BillingPeriodOverlap() =>
        new(Codes.BillingPeriodOverlap, "The specified date range overlaps with an existing billing period");

    public static Error BillingPeriodNotFound(Guid billingPeriodId) =>
        new(Codes.BillingPeriodNotFound, $"Billing period with ID {billingPeriodId} was not found");

    public static Error InvalidBillingPeriodState(string action, string currentState) =>
        new(Codes.InvalidBillingPeriodState, $"Cannot {action} billing period in {currentState} state");

    public static Error SettlementAlreadyExists(Guid billingPeriodId) =>
        new(Codes.SettlementAlreadyExists, $"Settlement already exists for billing period {billingPeriodId}");

    public static Error NoConsumptionsInPeriod() =>
        new(Codes.NoConsumptionsInPeriod, "No consumptions found in the billing period to generate settlement");

    public static Error NotFound(string entity, string message) =>
        new(Codes.NotFound, $"{entity}: {message}");

    public static Error Forbidden(string entity, string message) =>
        new(Codes.Forbidden, $"{entity}: {message}");

    public static Error SettlementNotFound(Guid settlementId) =>
        new(Codes.SettlementNotFound, $"Settlement with ID {settlementId} was not found");

    public static Error SettlementLineNotFound(Guid userId) =>
        new(Codes.SettlementLineNotFound, $"Settlement line for user {userId} was not found");

    public static Error PaymentAlreadyConfirmed(Guid userId) =>
        new(Codes.PaymentAlreadyConfirmed, $"Payment for user {userId} has already been confirmed");

    public static Error InvalidSettlementState(string action, string currentState) =>
        new(Codes.InvalidSettlementState, $"Cannot {action} settlement in {currentState} state");

    public static Error UserNotFound(Guid userId) =>
        new(Codes.UserNotFound, $"User with ID {userId} was not found");

    public static Error NotificationNotFound(Guid notificationId) =>
        new(Codes.NotificationNotFound, $"Notification with ID {notificationId} was not found");

    public static Error UnpaidSettlements(Guid userId) =>
        new(Codes.UnpaidSettlements, $"Cannot remove member with unpaid settlements. Please ensure all settlements are paid before removal.");
    public static Error Unauthenticated() =>
        new(Codes.Unauthenticated, "User is not authenticated");

    public static Error InvalidRequest(string propertyName) =>
        new(Codes.InvalidRequest, $"Property '{propertyName}' not found or invalid");

    public static Error NotMember() =>
        new(Codes.NotMember, "User is not a member of this space");

    public static Error NotAdmin() =>
        new(Codes.NotAdmin, "User is not an admin of this space");

    public static Error DomainError(string message) =>
        new(Codes.DomainError, message);

    public static Error ConfirmationFailed(string message) =>
        new(Codes.ConfirmationFailed, message);

    public static Error InvalidCoffeeType() =>
        new(Codes.InvalidCoffeeType, "Invalid coffee type");

    public static Error InvalidCoffeeTypeInPreset() =>
        new(Codes.InvalidCoffeeType, "Invalid coffee type in preset");

    public static Error InvalidConsumptionTime() =>
        new(Codes.InvalidConsumptionTime, "Consumption time cannot be in the future");

    public static Error StockNotFound(Guid spaceId) =>
        new(Codes.StockNotFound, $"No coffee stock found for space {spaceId}");

    public static Error StockLevelNotFound(Guid stockLevelId) =>
        new(Codes.StockLevelNotFound, $"Stock level {stockLevelId} not found");

    public static Error NoRemainingStock() =>
        new(Codes.NoRemainingStock, "No remaining stock to distribute");

    public static Error ProductNotFoundInStock() =>
        new(Codes.ProductNotFoundInStock, "Product not found in stock");

    public static Error InsufficientStock(string message) =>
        new(Codes.InsufficientStock, message);

    public static Error InvalidProductType() =>
        new(Codes.InvalidProductType, "Unknown coffee type");

    public static Error CannotUndoBagEmpty(string reason) =>
        new(Codes.CannotUndoBagEmpty, $"Cannot undo emptying this bag: {reason}");

    public static Error CurrencyConflict(string existingCurrency, string newCurrency) =>
        new(Codes.CurrencyConflict, $"Stock uses {existingCurrency} but purchase is in {newCurrency}. All purchases in a stock must use the same currency.");

    public static Error InvalidCurrency(string currencyCode) =>
        new(Codes.InvalidCurrency, $"'{currencyCode}' is not a valid ISO 4217 currency code");

    public static Error CsvImportFailed(string message) =>
        new(Codes.CsvImportFailed, message);

    public static Error CsvImportInvalidFormat(string message) =>
        new(Codes.CsvImportInvalidFormat, message);
}