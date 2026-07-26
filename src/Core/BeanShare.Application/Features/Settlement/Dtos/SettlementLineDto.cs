namespace BeanShare.Application.Features.Settlement.Dtos;
public sealed record SettlementLineDto(
    Guid UserId,
    string UserName,
    string UserEmail,
    decimal TotalCoffeeGrams,
    decimal? TotalMilkMl,
    decimal AmountDue,
    decimal AmountPaid,
    decimal NetBalance,
    string Currency,
    decimal ConsumptionPercentage,
    bool IsConfirmed,
    Guid? ConfirmedBy,
    DateTime? ConfirmedAt,
    string? RevolutTag = null
);
