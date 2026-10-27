using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Services;
using BeanShare.Application.Features.History.Services;
using BeanShare.Application.Features.Spaces.Services;
using BeanShare.Application.Services;
using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Aggregates.History;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.History.Commands.CommitHistoryImport;

public sealed class CommitHistoryImportHandler : IRequestHandler<CommitHistoryImportCommand, Result<HistoryImportResultDto>>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly IHistoryImportRepository _historyImportRepository;
    private readonly IUserService _userService;
    private readonly IUserRepository _userRepository;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public CommitHistoryImportHandler(
        ISpaceRepository spaceRepository,
        IBillingPeriodRepository billingPeriodRepository,
        IHistoryImportRepository historyImportRepository,
        IUserService userService,
        IUserRepository userRepository,
        IUserContext userContext,
        IClock clock)
    {
        _spaceRepository = spaceRepository;
        _billingPeriodRepository = billingPeriodRepository;
        _historyImportRepository = historyImportRepository;
        _userService = userService;
        _userRepository = userRepository;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<HistoryImportResultDto>> Handle(CommitHistoryImportCommand command, CancellationToken cancellationToken)
    {
        if ((PasteLimits.CheckLength(command.CupsText, nameof(command.CupsText)) ?? PasteLimits.CheckLength(command.PurchasesText, nameof(command.PurchasesText))) is { } tooLong)
            return Result<HistoryImportResultDto>.Failure(tooLong);

        var spaceId = new SpaceId(command.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(spaceId), cancellationToken);
        if (space is null)
            return Result<HistoryImportResultDto>.Failure(Error.SpaceNotFound(command.SpaceId));

        var members = await MemberResolver.CreateAsync(space, _userService, _userRepository, cancellationToken);
        var evaluation = HistoryPasteEvaluator.Evaluate(
            command.CupsText, command.PurchasesText, members, space.LedgerStartDate, command.CreateMissingPlaceholders);
        if (!evaluation.IsValid)
        {
            var errors = evaluation.Errors.Select(Error.HistoryImportInvalid)
                .Concat(evaluation.RowErrors.SelectMany(r => r.Errors.Select(e =>
                    Error.HistoryImportInvalid($"{r.Block} line {r.LineNumber}: {e}"))))
                .ToList();
            return Result<HistoryImportResultDto>.Failure(errors);
        }

        if ((command.Note ?? string.Empty).Trim().Length > HistoryImport.MaxNoteLength)
            return Result<HistoryImportResultDto>.Failure(Error.ValidationFailure("Note", $"At most {HistoryImport.MaxNoteLength} characters"));

        var created = new Dictionary<string, UserId>(StringComparer.OrdinalIgnoreCase);
        foreach (var text in evaluation.PlaceholdersToCreate)
        {
            var (name, email) = PlaceholderMembers.FromPastedText(text);
            var placeholder = await PlaceholderMembers.AddAsync(space, name, email, _userRepository, _clock, cancellationToken);
            if (placeholder.IsFailure)
                return Result<HistoryImportResultDto>.Failure(placeholder.Errors.ToArray());
            created[text] = placeholder.Value.Id;
        }

        var currentUserId = _userContext.CurrentUserId;
        var import = HistoryImport.Create(spaceId, currentUserId, command.Note, _clock);
        var periodIds = new Dictionary<string, BillingPeriodId>(StringComparer.OrdinalIgnoreCase);
        foreach (var period in evaluation.Periods)
        {
            var billingPeriod = BillingPeriod.CreateHistorical(
                spaceId, period.Label, period.StartDate, period.EndDate, import.Id, currentUserId, _clock);
            await _billingPeriodRepository.AddAsync(billingPeriod, cancellationToken);
            periodIds[period.Label] = billingPeriod.Id;
        }

        foreach (var row in evaluation.CupRows)
            import.AddCupCount(periodIds[row.Label], row.UserId ?? created[row.NewMemberText!], row.Cups);

        foreach (var row in evaluation.PurchaseRows)
        {
            UserId? payer = row.PaidBy ?? (row.NewPayerText is { } text ? created[text] : null);
            import.AddPurchase(periodIds[row.Label], row.PurchasedAt, payer, row.Description, row.Grams, row.Cost, space.Currency.Code);
        }

        await _historyImportRepository.AddAsync(import, cancellationToken);

        // Serializes with a ledger-start change, which must stay after every historical end.
        space.BumpLedgerVersion();
        await _spaceRepository.UpdateAsync(space, cancellationToken);

        return Result<HistoryImportResultDto>.Success(new HistoryImportResultDto(
            import.Id.Value,
            evaluation.Periods.Count,
            evaluation.CupRows.Sum(r => r.Cups),
            evaluation.PurchaseRows.Sum(r => r.Cost)));
    }
}
