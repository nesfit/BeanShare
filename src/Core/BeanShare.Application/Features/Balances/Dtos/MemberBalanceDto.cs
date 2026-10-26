namespace BeanShare.Application.Features.Balances.Dtos;

/// <summary>A member's running balance and how it is made up; <see cref="Balance"/> is the calculator's Net.</summary>
public sealed record MemberBalanceDto(
    Guid UserId,
    string UserName,
    bool IsCurrentMember,
    bool IsTreasurer,
    decimal Cost,
    decimal Credit,
    decimal Carried,
    decimal Adjustments,
    decimal Rounding,
    decimal Balance);
