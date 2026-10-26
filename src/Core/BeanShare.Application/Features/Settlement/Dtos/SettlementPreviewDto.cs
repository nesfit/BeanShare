namespace BeanShare.Application.Features.Settlement.Dtos;

/// <summary>What generating the settlement of a period would produce right now; nothing is saved.</summary>
public sealed record SettlementPreviewDto(
    Guid BillingPeriodId,
    string BillingPeriodName,
    string Currency,
    Guid TreasurerUserId,
    string TreasurerName,
    decimal TotalAmount,
    List<SettlementLineDto> Lines,
    IReadOnlyList<SettlementTransferDto> Transfers);
