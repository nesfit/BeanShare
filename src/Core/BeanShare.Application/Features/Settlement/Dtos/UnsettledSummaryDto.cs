namespace BeanShare.Application.Features.Settlement.Dtos;

/// <summary>
/// What has piled up since the last settlement, one entry per space the caller belongs to.
/// </summary>
public sealed record UnsettledSummaryResult(IReadOnlyList<UnsettledSpaceSummaryDto> Spaces)
{
    /// <summary>Spaces with something to settle — unbilled cups or a non-zero balance — what a UI should render.</summary>
    public IReadOnlyList<UnsettledSpaceSummaryDto> ActiveSpaces =>
        Spaces.Where(s => s.TotalCups > 0 || s.MyBalance != 0 || s.Members.Any(m => m.Balance != 0)).ToList();

    public int MyCups => Spaces.Sum(s => s.MyCups);
}

/// <summary>
/// Per-space preview of the next settlement: the cups no settlement has covered yet and each member's
/// running balance (carried in + balance entries + credit − cost), computed by the same calculator as a
/// real settlement, as of now.
/// </summary>
/// <param name="SettledThrough">
/// End of the last settled billing period. <c>null</c> when the space has never been settled.
/// </param>
/// <param name="MyPaid">Informational: what the caller paid for bags since the last ledger settlement.</param>
/// <param name="Members">
/// Per-member breakdown. Populated for space admins only; empty for regular members, who only
/// get their own numbers.
/// </param>
/// <param name="MyBalance">The caller's running balance; negative means they owe.</param>
/// <param name="UnassignedRounding">
/// Admins only: the rounding residual no line carries while no treasurer is set (balances + it = 0).
/// </param>
public sealed record UnsettledSpaceSummaryDto(
    Guid SpaceId,
    string SpaceName,
    string CurrencyCode,
    DateTime? SettledThrough,
    string? LastSettledPeriodName,
    DateTime? LastSettledAt,
    string? CurrentPeriodName,
    bool IsAdmin,
    int MyCups,
    decimal MyGrams,
    decimal MyCost,
    decimal MyPaid,
    int TotalCups,
    decimal TotalGrams,
    decimal TotalCost,
    IReadOnlyList<UnsettledMemberSummaryDto> Members,
    decimal MyCredit = 0m,
    decimal MyCarried = 0m,
    decimal MyAdjustments = 0m,
    decimal MyRounding = 0m,
    decimal MyBalance = 0m,
    decimal UnassignedRounding = 0m,
    decimal MyMergeTransfer = 0m)
{
    public bool HasEverBeenSettled => SettledThrough.HasValue;
}

/// <param name="Paid">Informational: bags this member paid for since the last ledger settlement.</param>
/// <param name="Balance">The member's running balance; negative means they owe.</param>
public sealed record UnsettledMemberSummaryDto(
    Guid UserId,
    string UserName,
    int Cups,
    decimal Grams,
    decimal Cost,
    decimal Paid,
    bool IsCurrentUser,
    decimal Credit = 0m,
    decimal Carried = 0m,
    decimal Adjustments = 0m,
    decimal Rounding = 0m,
    decimal Balance = 0m,
    decimal MergeTransfer = 0m);
