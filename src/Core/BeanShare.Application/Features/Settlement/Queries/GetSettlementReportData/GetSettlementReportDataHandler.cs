using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Settlement.Dtos;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Settlement.Queries.GetSettlementReportData;
public sealed class GetSettlementReportDataHandler : IRequestHandler<GetSettlementReportDataQuery, Result<SettlementReportData>>
{
    private readonly ISettlementRepository _settlementRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserService _userService;
    private readonly IUserContext _userContext;

    public GetSettlementReportDataHandler(
        ISettlementRepository settlementRepository,
        IBillingPeriodRepository billingPeriodRepository,
        ISpaceRepository spaceRepository,
        IUserService userService,
        IUserContext userContext)
    {
        _settlementRepository = settlementRepository;
        _billingPeriodRepository = billingPeriodRepository;
        _spaceRepository = spaceRepository;
        _userService = userService;
        _userContext = userContext;
    }

    public async Task<Result<SettlementReportData>> Handle(GetSettlementReportDataQuery request, CancellationToken cancellationToken)
    {
        var settlement = await _settlementRepository.GetByIdAsync(request.SettlementId, cancellationToken);

        if (settlement is null)
        {
            return Result<SettlementReportData>.Failure(Error.SettlementNotFound(request.SettlementId.Value));
        }

        var billingPeriod = await _billingPeriodRepository.GetByIdAsync(settlement.BillingPeriodId, cancellationToken);
        if (billingPeriod is null)
        {
            return Result<SettlementReportData>.Failure(Error.BillingPeriodNotFound(settlement.BillingPeriodId.Value));
        }

        var space = await _spaceRepository.GetSingleBySpecAsync(
            new SpaceByIdSpecification(settlement.SpaceId),
            cancellationToken);

        if (space is null)
        {
            return Result<SettlementReportData>.Failure(Error.SpaceNotFound(settlement.SpaceId.Value));
        }

        if (!space.HasMember(_userContext.CurrentUserId))
        {
            return Result<SettlementReportData>.Failure(Error.InsufficientSpacePrivileges("view settlement report"));
        }

        var allUserIds = settlement.Lines.Select(l => l.UserId).ToList();
        allUserIds.Add(settlement.GeneratedBy);
        if (settlement.TreasurerUserId is { } treasurerId)
            allUserIds.Add(treasurerId);

        var confirmedByIds = settlement.Lines
            .Where(l => l.Confirmation != null)
            .Select(l => l.Confirmation!.ConfirmedBy)
            .ToList();
        allUserIds.AddRange(confirmedByIds);

        var users = await _userService.GetByIdsAsync(allUserIds.Distinct(), cancellationToken);
        var userLookup = users.ToDictionary(u => u.Id);

        var generatedByName = userLookup.TryGetValue(settlement.GeneratedBy, out var generatedByUser)
            ? generatedByUser.Name
            : "Unknown";

        var totalCoffeeGrams = settlement.Lines.Sum(l => l.TotalCoffeeGrams);
        var totalMilkMl = settlement.Lines.Sum(l => l.TotalMilkMl ?? 0);

        var treasurerName = settlement.TreasurerUserId is { } treasurer && userLookup.TryGetValue(treasurer, out var treasurerUser)
            ? treasurerUser.Name
            : null;

        var lines = new List<SettlementLineReportData>();
        foreach (var line in settlement.Lines.OrderBy(l => l.NetAmount ?? 0m))
        {
            userLookup.TryGetValue(line.UserId, out var user);
            var userName = user?.Name ?? $"User {line.UserId.Value}";
            var userEmail = user?.Email ?? "";

            var consumptionPercentage = totalCoffeeGrams > 0
                ? (line.TotalCoffeeGrams / totalCoffeeGrams) * 100
                : 0;

            string? confirmedByName = null;
            if (line.Confirmation != null && userLookup.TryGetValue(line.Confirmation.ConfirmedBy, out var confirmer))
            {
                confirmedByName = confirmer.Name;
            }

            lines.Add(new SettlementLineReportData(
                userName,
                userEmail,
                line.TotalCoffeeGrams,
                line.TotalMilkMl,
                consumptionPercentage,
                line.AmountDue.Amount,
                line.IsConfirmed,
                confirmedByName,
                line.Confirmation?.ConfirmedAt,
                line.CreditAmount,
                line.CarriedInAmount,
                line.AdjustmentsAmount,
                line.RoundingAmount,
                line.NetAmount,
                line.Resolution?.ToString(),
                TransferText(settlement, line, treasurerName)
            ));
        }

        var reportData = new SettlementReportData(
            space.Name,
            billingPeriod.Name,
            billingPeriod.StartDate,
            billingPeriod.EndDate,
            settlement.GeneratedAt,
            generatedByName,
            settlement.TotalAmount,
            settlement.Currency,
            settlement.Lines.Count,
            totalCoffeeGrams,
            totalMilkMl,
            lines,
            settlement.IsLedger,
            treasurerName,
            settlement.SupersededBySettlementId.HasValue
        );

        return Result<SettlementReportData>.Success(reportData);
    }

    /// <summary>"Pay {T} X" / "{T} pays X" for a ledger line; empty for the treasurer, a zero net or a legacy line.</summary>
    private static string? TransferText(Domain.Aggregates.Settlement.Settlement settlement, Domain.Aggregates.Settlement.SettlementLine line, string? treasurerName)
    {
        if (!settlement.IsLedger || settlement.IsTreasurerLine(line) || line.NetAmount is not { } net || net == 0)
            return null;

        var amount = BeanShare.Domain.ValueObjects.Currency.Create(settlement.Currency).FormatAmount(Math.Abs(net));
        var treasurer = treasurerName ?? "the treasurer";
        return net < 0 ? $"Pay {treasurer} {amount}" : $"{treasurer} pays {amount}";
    }
}
