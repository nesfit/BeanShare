namespace BeanShare.Application.Features.Balances.Dtos;

/// <summary>
/// A balance entry: +<see cref="Amount"/> for <see cref="UserId"/>, −<see cref="Amount"/> for the counterparty.
/// <see cref="ViewerAmount"/> is the same entry seen from the caller's side (null when not a party).
/// </summary>
public sealed record BalanceEntryDto(
    Guid Id,
    Guid SpaceId,
    Guid UserId,
    string UserName,
    Guid CounterpartyUserId,
    string CounterpartyName,
    decimal Amount,
    string Currency,
    DateTime EffectiveDate,
    string Kind,
    string Note,
    Guid CreatedBy,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid? IncludedInSettlementId,
    bool IsLocked,
    decimal? ViewerAmount);
