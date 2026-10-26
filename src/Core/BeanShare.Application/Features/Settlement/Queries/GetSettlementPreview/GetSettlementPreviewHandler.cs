using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Services;
using BeanShare.Application.Features.Settlement.Commands.GenerateSettlement;
using BeanShare.Application.Features.Settlement.Dtos;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Services;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Settlement.Queries.GetSettlementPreview;

public sealed class GetSettlementPreviewHandler : IRequestHandler<GetSettlementPreviewQuery, Result<SettlementPreviewDto>>
{
    private readonly ISettlementRepository _settlementRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly LedgerSnapshotService _ledgerSnapshotService;
    private readonly IUserService _userService;
    private readonly IUserContext _userContext;
    private readonly IUserRepository _userRepository;

    public GetSettlementPreviewHandler(
        ISettlementRepository settlementRepository,
        IBillingPeriodRepository billingPeriodRepository,
        ISpaceRepository spaceRepository,
        LedgerSnapshotService ledgerSnapshotService,
        IUserService userService,
        IUserContext userContext,
        IUserRepository userRepository)
    {
        _userRepository = userRepository;
        _settlementRepository = settlementRepository;
        _billingPeriodRepository = billingPeriodRepository;
        _spaceRepository = spaceRepository;
        _ledgerSnapshotService = ledgerSnapshotService;
        _userService = userService;
        _userContext = userContext;
    }

    public async Task<Result<SettlementPreviewDto>> Handle(GetSettlementPreviewQuery query, CancellationToken cancellationToken)
    {
        var periodId = new BillingPeriodId(query.BillingPeriodId);
        var period = await _billingPeriodRepository.GetByIdAsync(periodId, cancellationToken);
        if (period is null)
            return Result<SettlementPreviewDto>.Failure(Error.BillingPeriodNotFound(query.BillingPeriodId));

        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(period.SpaceId), cancellationToken);
        if (space is null)
            return Result<SettlementPreviewDto>.Failure(Error.SpaceNotFound(period.SpaceId.Value));

        if (!space.IsAdmin(_userContext.CurrentUserId))
            return Result<SettlementPreviewDto>.Failure(Error.InsufficientSpacePrivileges("preview settlements"));

        var spacePeriods = await _billingPeriodRepository.GetBySpaceIdAsync(space.Id, cancellationToken);
        if (await SettlementGuards.CheckAsync(space, period, spacePeriods, _userRepository, cancellationToken) is { } guardError)
            return Result<SettlementPreviewDto>.Failure(guardError);

        if (await _settlementRepository.GetByBillingPeriodIdAsync(periodId, cancellationToken) is not null)
            return Result<SettlementPreviewDto>.Failure(Error.SettlementAlreadyExists(query.BillingPeriodId));

        var snapshot = await _ledgerSnapshotService.ForPeriodAsync(space, period, forUpdate: false, cancellationToken);
        var treasurerId = space.TreasurerUserId!.Value;
        var lines = snapshot.Calculation.Lines;

        var users = (await _userService.GetByIdsAsync(lines.Select(l => l.UserId).Append(treasurerId).Distinct(), cancellationToken))
            .ToDictionary(u => u.Id);
        string NameOf(UserId id) => users.TryGetValue(id, out var u) ? u.Name : $"User {id.Value}";

        var totalGrams = lines.Sum(l => l.CoffeeGrams);
        var lineDtos = lines
            .Select(l => new SettlementLineDto(
                l.UserId.Value,
                NameOf(l.UserId),
                users.TryGetValue(l.UserId, out var user) ? user.PublicEmail : string.Empty,
                l.CoffeeGrams,
                l.MilkMl ?? 0,
                l.Cost,
                l.Credit,
                l.Net,
                space.Currency.Code,
                totalGrams > 0 ? l.CoffeeGrams / totalGrams * 100 : 0,
                false,
                null,
                null,
                user?.RevolutTag,
                l.Credit,
                l.CarriedIn,
                l.Adjustments,
                l.Rounding,
                Domain.Enums.LineResolution.Open.ToString(),
                null,
                l.UserId == treasurerId,
                l.MergeTransfer,
                user?.MergedIntoUserId is { } mergedInto ? NameOf(mergedInto) : null))
            .OrderBy(l => l.NetBalance)
            .ThenBy(l => l.UserName)
            .ToList();

        var transfers = SettlementLedgerCalculator
            .StarTransfers(lines.Select(l => (l.UserId, l.Net)), treasurerId)
            .Select(t => new SettlementTransferDto(t.From.Value, NameOf(t.From), t.To.Value, NameOf(t.To), t.Amount))
            .ToList();

        return Result<SettlementPreviewDto>.Success(new SettlementPreviewDto(
            period.Id.Value,
            period.Name,
            space.Currency.Code,
            treasurerId.Value,
            NameOf(treasurerId),
            lines.Sum(l => l.Cost),
            lineDtos,
            transfers));
    }
}
