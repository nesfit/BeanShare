namespace BeanShare.Contracts.Settlement;

/// <summary>One payment of the star plan: every debtor pays the treasurer, the treasurer pays every creditor.</summary>
public record SettlementTransferDto(
    Guid FromUserId,
    string FromName,
    Guid ToUserId,
    string ToName,
    decimal Amount
);
