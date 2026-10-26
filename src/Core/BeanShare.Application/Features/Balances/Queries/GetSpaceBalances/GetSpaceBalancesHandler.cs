using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Dtos;
using BeanShare.Application.Features.Balances.Services;
using BeanShare.Application.Services;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Balances.Queries.GetSpaceBalances;

public sealed class GetSpaceBalancesHandler : IRequestHandler<GetSpaceBalancesQuery, Result<SpaceBalancesDto>>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly IBalanceEntryRepository _balanceEntryRepository;
    private readonly ISettlementRepository _settlementRepository;
    private readonly LedgerSnapshotService _ledgerSnapshotService;
    private readonly IUserService _userService;
    private readonly IUserContext _userContext;

    public GetSpaceBalancesHandler(
        ISpaceRepository spaceRepository,
        IBalanceEntryRepository balanceEntryRepository,
        ISettlementRepository settlementRepository,
        LedgerSnapshotService ledgerSnapshotService,
        IUserService userService,
        IUserContext userContext)
    {
        _ledgerSnapshotService = ledgerSnapshotService;
        _spaceRepository = spaceRepository;
        _balanceEntryRepository = balanceEntryRepository;
        _settlementRepository = settlementRepository;
        _userService = userService;
        _userContext = userContext;
    }

    public async Task<Result<SpaceBalancesDto>> Handle(GetSpaceBalancesQuery query, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(query.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(spaceId), cancellationToken);
        if (space is null)
            return Result<SpaceBalancesDto>.Failure(Error.SpaceNotFound(query.SpaceId));

        var currentUserId = _userContext.CurrentUserId;
        var isAdmin = space.IsAdmin(currentUserId);

        var entries = await _balanceEntryRepository.GetVisibleAsync(spaceId, isAdmin ? null : currentUserId, cancellationToken);
        var entryDtos = await BalanceEntryMapping.ToDtosAsync(
            entries.OrderByDescending(e => e.EffectiveDate).ThenByDescending(e => e.CreatedAt).ToList(),
            currentUserId, _userService, cancellationToken);

        var settlements = await _settlementRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        var openLegacyLines = settlements
            .Where(s => !s.IsLedger)
            .SelectMany(s => s.Lines)
            .Count(l => !l.IsConfirmed && (isAdmin || l.UserId == currentUserId));

        var settings = await LedgerSettingsMapping.ToDtoAsync(
            space, settlements.Any(s => s.IsLedger), _userService, cancellationToken);

        var snapshot = await _ledgerSnapshotService.RunningAsync(space, cancellationToken);
        var members = await BuildMemberBalancesAsync(space, snapshot.Calculation, isAdmin, currentUserId, cancellationToken);

        return Result<SpaceBalancesDto>.Success(new SpaceBalancesDto(
            space.Id.Value, space.Currency.Code, isAdmin, settings, entryDtos, openLegacyLines, members,
            isAdmin ? snapshot.Calculation.UnassignedRounding : 0m));
    }

    /// <summary>Admins: every current member and anyone else with a position. Members: their own row.</summary>
    private async Task<IReadOnlyList<MemberBalanceDto>> BuildMemberBalancesAsync(
        Domain.Aggregates.Space.Space space,
        Domain.Services.LedgerCalculation calculation,
        bool isAdmin,
        Domain.Common.UserId currentUserId,
        CancellationToken cancellationToken)
    {
        var userIds = isAdmin
            ? space.Members.Select(m => m.UserId).Concat(calculation.Lines.Select(l => l.UserId)).Distinct().ToList()
            : [currentUserId];

        var names = (await _userService.GetByIdsAsync(userIds, cancellationToken)).ToDictionary(u => u.Id, u => u.Name);

        return userIds
            .Select(id =>
            {
                var line = calculation.LineFor(id);
                return new MemberBalanceDto(
                    id.Value,
                    names.TryGetValue(id, out var name) ? name : $"User {id.Value}",
                    space.HasMember(id),
                    space.IsTreasurer(id),
                    line?.Cost ?? 0m,
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
