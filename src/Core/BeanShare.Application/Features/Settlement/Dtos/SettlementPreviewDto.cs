namespace BeanShare.Application.Features.Settlement.Dtos;

/// <summary>What generating the settlement of a period would produce right now; nothing is saved.</summary>
/// <param name="FallbackRateMissing">Some cups have no bag price and no fallback price, so they cost nothing (H5).</param>
public sealed record SettlementPreviewDto(
    Guid BillingPeriodId,
    string BillingPeriodName,
    string Currency,
    Guid TreasurerUserId,
    string TreasurerName,
    decimal TotalAmount,
    List<SettlementLineDto> Lines,
    IReadOnlyList<SettlementTransferDto> Transfers,
    bool FallbackRateMissing = false);
