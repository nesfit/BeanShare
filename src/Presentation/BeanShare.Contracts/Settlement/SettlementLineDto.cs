namespace BeanShare.Contracts.Settlement;

public record SettlementLineDto(
    Guid UserId,
    string UserName,
    string UserEmail,
    decimal TotalCoffeeGrams,
    decimal? TotalMilkMl,
    decimal AmountDue,
    decimal AmountPaid,
    decimal NetBalance,
    decimal ConsumptionPercentage,
    bool IsConfirmed,
    Guid? ConfirmedBy,
    DateTime? ConfirmedAt,
    string? RevolutTag = null,
    decimal? Credit = null,
    decimal? CarriedIn = null,
    decimal? Adjustments = null,
    decimal? Rounding = null,
    string? Resolution = null,
    Guid? CarriedToSettlementId = null,
    bool IsTreasurer = false,
    decimal MergeTransfer = 0m,
    string? MergedIntoName = null
);
