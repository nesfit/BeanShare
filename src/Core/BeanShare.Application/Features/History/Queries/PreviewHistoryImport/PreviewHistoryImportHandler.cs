using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Services;
using BeanShare.Application.Features.History.Dtos;
using BeanShare.Application.Features.History.Services;
using BeanShare.Application.Services;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.History.Queries.PreviewHistoryImport;

public sealed class PreviewHistoryImportHandler : IRequestHandler<PreviewHistoryImportQuery, Result<HistoryImportPreviewDto>>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserService _userService;
    private readonly IUserRepository _userRepository;

    public PreviewHistoryImportHandler(ISpaceRepository spaceRepository, IUserService userService, IUserRepository userRepository)
    {
        _spaceRepository = spaceRepository;
        _userService = userService;
        _userRepository = userRepository;
    }

    public async Task<Result<HistoryImportPreviewDto>> Handle(PreviewHistoryImportQuery query, CancellationToken cancellationToken)
    {
        if ((PasteLimits.CheckLength(query.CupsText, nameof(query.CupsText)) ?? PasteLimits.CheckLength(query.PurchasesText, nameof(query.PurchasesText))) is { } tooLong)
            return Result<HistoryImportPreviewDto>.Failure(tooLong);

        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(new SpaceId(query.SpaceId)), cancellationToken);
        if (space is null)
            return Result<HistoryImportPreviewDto>.Failure(Error.SpaceNotFound(query.SpaceId));

        var members = await MemberResolver.CreateAsync(space, _userService, _userRepository, cancellationToken);
        var evaluation = HistoryPasteEvaluator.Evaluate(
            query.CupsText, query.PurchasesText, members, space.LedgerStartDate, query.CreateMissingPlaceholders);

        return Result<HistoryImportPreviewDto>.Success(ToPreview(evaluation, space.Currency.Code));
    }

    internal static HistoryImportPreviewDto ToPreview(HistoryPasteEvaluator.Evaluation evaluation, string currency)
    {
        var periods = evaluation.Periods
            .Select(p => new HistoryPreviewPeriodDto(
                p.Label,
                p.StartDate,
                p.EndDate,
                evaluation.CupRows.Where(r => Same(r.Label, p.Label)).Sum(r => r.Cups),
                evaluation.PurchaseRows.Where(r => Same(r.Label, p.Label)).Sum(r => r.Cost),
                evaluation.PurchaseRows.Count(r => Same(r.Label, p.Label))))
            .ToList();

        var memberTotals = evaluation.CupRows
            .GroupBy(r => r.UserId?.Value.ToString() ?? "new:" + r.MemberName.ToLowerInvariant())
            .Select(g => new HistoryPreviewMemberDto(g.First().MemberName, g.First().UserId?.Value, g.Sum(r => r.Cups), g.First().UserId is null))
            .OrderByDescending(m => m.Cups)
            .ThenBy(m => m.Member, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new HistoryImportPreviewDto(
            periods,
            memberTotals,
            evaluation.RowErrors,
            evaluation.Errors,
            evaluation.PlaceholdersToCreate,
            evaluation.CupRows.Sum(r => r.Cups),
            evaluation.PurchaseRows.Sum(r => r.Cost),
            currency,
            evaluation.Periods.Count > 0 ? evaluation.Periods.Min(p => p.StartDate) : null,
            evaluation.Periods.Count > 0 ? evaluation.Periods.Max(p => p.EndDate) : null);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
