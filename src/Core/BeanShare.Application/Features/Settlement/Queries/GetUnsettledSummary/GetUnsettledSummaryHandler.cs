using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Services;
using BeanShare.Application.Features.Settlement.Dtos;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Settlement.Queries.GetUnsettledSummary;

/// <summary>
/// Previews the next settlement as of now: the unsettled cups and every member's running balance, both
/// straight from <see cref="LedgerSnapshotService"/> (the settlement calculator). "Paid" is informational.
/// </summary>
public sealed class GetUnsettledSummaryHandler
    : IRequestHandler<GetUnsettledSummaryQuery, Result<UnsettledSummaryResult>>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly LedgerSnapshotService _ledgerSnapshotService;
    private readonly IUserService _userService;
    private readonly IUserContext _userContext;

    public GetUnsettledSummaryHandler(
        ISpaceRepository spaceRepository,
        IBillingPeriodRepository billingPeriodRepository,
        LedgerSnapshotService ledgerSnapshotService,
        IUserService userService,
        IUserContext userContext)
    {
        _spaceRepository = spaceRepository;
        _billingPeriodRepository = billingPeriodRepository;
        _ledgerSnapshotService = ledgerSnapshotService;
        _userService = userService;
        _userContext = userContext;
    }

    public async Task<Result<UnsettledSummaryResult>> Handle(
        GetUnsettledSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var currentUserId = _userContext.CurrentUserId;

        IReadOnlyList<Domain.Aggregates.Space.Space> spaces;
        if (request.SpaceId.HasValue)
        {
            var space = await _spaceRepository.GetByIdAsync(new SpaceId(request.SpaceId.Value), cancellationToken);
            if (space is null)
            {
                return Result<UnsettledSummaryResult>.Failure(Error.SpaceNotFound(request.SpaceId.Value));
            }

            if (!space.HasMember(currentUserId))
            {
                return Result<UnsettledSummaryResult>.Failure(
                    Error.InsufficientSpacePrivileges("view unsettled consumption"));
            }

            spaces = [space];
        }
        else
        {
            spaces = await _spaceRepository.GetBySpecAsync(
                new SpacesWithUserMembershipSpecification(currentUserId), cancellationToken);
        }

        var summaries = new List<UnsettledSpaceSummaryDto>(spaces.Count);
        foreach (var space in spaces)
        {
            summaries.Add(await BuildSummaryAsync(space, currentUserId, cancellationToken));
        }

        return Result<UnsettledSummaryResult>.Success(new UnsettledSummaryResult(summaries));
    }

    private async Task<UnsettledSpaceSummaryDto> BuildSummaryAsync(
        Domain.Aggregates.Space.Space space,
        UserId currentUserId,
        CancellationToken cancellationToken)
    {
        var periods = await _billingPeriodRepository.GetBySpaceIdAsync(space.Id, cancellationToken);
        // Imported history is statistics; the ledger has settled only live periods.
        var lastSettled = periods
            .Where(p => p.State == BillingState.Settled && !p.IsHistorical)
            .OrderByDescending(p => p.EndDate)
            .FirstOrDefault();
        var currentPeriod = periods.FirstOrDefault(p => p.State == BillingState.Open);

        var snapshot = await _ledgerSnapshotService.RunningAsync(space, cancellationToken);
        var calculation = snapshot.Calculation;

        var cupsByUser = snapshot.Cups
            .GroupBy(c => c.UserId)
            .ToDictionary(g => g.Key, g => (Cups: g.Count(), Grams: g.Sum(c => c.Quantity.Grams)));

        // Informational only: bags paid for since the last ledger settlement (and not before the ledger start).
        var lastLedgerEnd = snapshot.PreviousSettlement is { } previous
            ? periods.FirstOrDefault(p => p.Id == previous.BillingPeriodId)?.EndDate
            : null;
        var currency = space.Currency;
        var paidByUser = (snapshot.Stock?.Purchases ?? [])
            .Where(p => p.Cost.Currency.Code == currency.Code)
            .Where(p => space.LedgerStartDate is not { } start || p.PurchasedAt >= start)
            .Where(p => lastLedgerEnd is not { } end || p.PurchasedAt > end)
            .GroupBy(p => p.EffectivePayer)
            .ToDictionary(g => g.Key, g => Math.Round(g.Sum(p => p.Cost.Amount), currency.DecimalPlaces));

        var isAdmin = space.IsAdmin(currentUserId);
        var mine = calculation.LineFor(currentUserId);
        var myCups = cupsByUser.GetValueOrDefault(currentUserId);

        var members = isAdmin
            ? await BuildMemberRowsAsync(calculation, cupsByUser, paidByUser, currentUserId, cancellationToken)
            : [];

        return new UnsettledSpaceSummaryDto(
            space.Id.Value,
            space.Name,
            currency.Code,
            lastSettled?.EndDate,
            lastSettled?.Name,
            lastSettled?.SettledAt,
            currentPeriod?.Name,
            isAdmin,
            myCups.Cups,
            myCups.Grams,
            mine?.Cost ?? 0m,
            paidByUser.GetValueOrDefault(currentUserId),
            snapshot.Cups.Count,
            snapshot.Cups.Sum(c => c.Quantity.Grams),
            calculation.Lines.Sum(l => l.Cost),
            members,
            mine?.Credit ?? 0m,
            mine?.CarriedIn ?? 0m,
            mine?.Adjustments ?? 0m,
            mine?.Rounding ?? 0m,
            calculation.NetFor(currentUserId),
            isAdmin ? calculation.UnassignedRounding : 0m,
            mine?.MergeTransfer ?? 0m);
    }

    private async Task<IReadOnlyList<UnsettledMemberSummaryDto>> BuildMemberRowsAsync(
        Domain.Services.LedgerCalculation calculation,
        IReadOnlyDictionary<UserId, (int Cups, decimal Grams)> cupsByUser,
        IReadOnlyDictionary<UserId, decimal> paidByUser,
        UserId currentUserId,
        CancellationToken cancellationToken)
    {
        // Consumers, payers, entry parties and carry holders: everyone with a position.
        var userIds = calculation.Lines.Select(l => l.UserId)
            .Concat(cupsByUser.Keys)
            .Concat(paidByUser.Keys)
            .Distinct()
            .ToList();

        if (userIds.Count == 0)
        {
            return [];
        }

        var users = await _userService.GetByIdsAsync(userIds, cancellationToken);
        var names = users.ToDictionary(u => u.Id, u => u.Name);

        return userIds
            .Select(id =>
            {
                var line = calculation.LineFor(id);
                var cups = cupsByUser.GetValueOrDefault(id);
                return new UnsettledMemberSummaryDto(
                    id.Value,
                    names.TryGetValue(id, out var name) ? name : $"User {id.Value}",
                    cups.Cups,
                    cups.Grams,
                    line?.Cost ?? 0m,
                    paidByUser.GetValueOrDefault(id),
                    id == currentUserId,
                    line?.Credit ?? 0m,
                    line?.CarriedIn ?? 0m,
                    line?.Adjustments ?? 0m,
                    line?.Rounding ?? 0m,
                    line?.Net ?? 0m,
                    line?.MergeTransfer ?? 0m);
            })
            .OrderBy(m => m.Balance)
            .ThenBy(m => m.UserName)
            .ToList();
    }
}
