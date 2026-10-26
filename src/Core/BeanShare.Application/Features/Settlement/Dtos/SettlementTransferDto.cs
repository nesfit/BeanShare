namespace BeanShare.Application.Features.Settlement.Dtos;

/// <summary>One payment of the star plan: every debtor pays the treasurer, the treasurer pays every creditor.</summary>
public sealed record SettlementTransferDto(
    Guid FromUserId,
    string FromName,
    Guid ToUserId,
    string ToName,
    decimal Amount);
