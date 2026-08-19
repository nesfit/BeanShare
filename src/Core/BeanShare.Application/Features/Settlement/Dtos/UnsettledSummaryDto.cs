namespace BeanShare.Application.Features.Settlement.Dtos;

/// <summary>
/// What has piled up since the last settlement, one entry per space the caller belongs to.
/// </summary>
public sealed record UnsettledSummaryResult(IReadOnlyList<UnsettledSpaceSummaryDto> Spaces)
{
    /// <summary>Spaces that actually have something unbilled — what a UI should render.</summary>
    public IReadOnlyList<UnsettledSpaceSummaryDto> ActiveSpaces =>
        Spaces.Where(s => s.TotalCups > 0).ToList();

    public int MyCups => Spaces.Sum(s => s.MyCups);
}

/// <summary>
/// Per-space tally of the cups that no settlement has covered yet: everything logged after the
/// most recent settled billing period, plus anything still sitting outside a settled period.
/// Costs use the same rules as a real settlement (each cup's stamped per-bag cost, falling back
/// to a weighted average for legacy cups), so these numbers preview the next settlement.
/// </summary>
/// <param name="SettledThrough">
/// End of the last settled billing period — the "since" date. <c>null</c> when the space has
/// never been settled, in which case the tally covers all of its history.
/// </param>
/// <param name="Members">
/// Per-member breakdown. Populated for space admins only; empty for regular members, who only
/// get their own <see cref="MyCups"/> / <see cref="MyCost"/>.
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
    IReadOnlyList<UnsettledMemberSummaryDto> Members)
{
    public bool HasEverBeenSettled => SettledThrough.HasValue;

    /// <summary>What the caller has paid into the space minus what they owe — negative means they owe.</summary>
    public decimal MyBalance => MyPaid - MyCost;
}

public sealed record UnsettledMemberSummaryDto(
    Guid UserId,
    string UserName,
    int Cups,
    decimal Grams,
    decimal Cost,
    decimal Paid,
    bool IsCurrentUser)
{
    public decimal Balance => Paid - Cost;
}
