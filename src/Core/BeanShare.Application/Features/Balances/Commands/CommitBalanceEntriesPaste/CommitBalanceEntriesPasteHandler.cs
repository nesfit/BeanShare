using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Dtos;
using BeanShare.Application.Features.Balances.Services;
using BeanShare.Application.Features.Spaces.Services;
using BeanShare.Application.Services;
using BeanShare.Domain.Aggregates.Ledger;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Balances.Commands.CommitBalanceEntriesPaste;

public sealed class CommitBalanceEntriesPasteHandler
    : IRequestHandler<CommitBalanceEntriesPasteCommand, Result<IReadOnlyList<BalanceEntryDto>>>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly IBalanceEntryRepository _balanceEntryRepository;
    private readonly IUserService _userService;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly IUserRepository _userRepository;

    public CommitBalanceEntriesPasteHandler(
        ISpaceRepository spaceRepository,
        IBalanceEntryRepository balanceEntryRepository,
        IUserService userService,
        IUserContext userContext,
        IClock clock,
        IUserRepository userRepository)
    {
        _userRepository = userRepository;
        _spaceRepository = spaceRepository;
        _balanceEntryRepository = balanceEntryRepository;
        _userService = userService;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<BalanceEntryDto>>> Handle(CommitBalanceEntriesPasteCommand command, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(command.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(spaceId), cancellationToken);
        if (space is null)
            return Result<IReadOnlyList<BalanceEntryDto>>.Failure(Error.SpaceNotFound(command.SpaceId));

        var currentUserId = _userContext.CurrentUserId;
        var members = await MemberResolver.CreateAsync(space, _userService, _userRepository, cancellationToken);
        var batch = BalancePasteEvaluator.CreateBatch(
            space, currentUserId, command.CounterpartyUserId, command.EffectiveDate, command.Kind, command.BatchNote, _clock);
        var evaluation = BalancePasteEvaluator.Evaluate(command.Text, batch, members, _clock, command.CreateMissingPlaceholders);

        if (!evaluation.IsValid)
        {
            var errors = evaluation.Errors.Select(e => Error.ValidationFailure("Paste", e))
                .Concat(evaluation.Rows.SelectMany(r => r.Errors.Select(e => Error.ValidationFailure($"Line {r.LineNumber}", e))))
                .ToList();
            return Result<IReadOnlyList<BalanceEntryDto>>.Failure(errors);
        }

        // New placeholders join in the same unit of work, so the batch stays all or nothing (U10).
        var created = new Dictionary<string, UserId>(StringComparer.OrdinalIgnoreCase);
        foreach (var text in evaluation.PlaceholdersToCreate)
        {
            var (name, email) = PlaceholderMembers.FromPastedText(text);
            var placeholder = await PlaceholderMembers.AddAsync(space, name, email, _userRepository, _clock, cancellationToken);
            if (placeholder.IsFailure)
                return Result<IReadOnlyList<BalanceEntryDto>>.Failure(placeholder.Errors.ToArray());
            created[text] = placeholder.Value.Id;
        }

        var entries = new List<BalanceEntry>();
        foreach (var row in evaluation.Rows)
        {
            var memberId = row.IsNewPlaceholder ? created[row.MemberText.Trim()] : new UserId(row.UserId!.Value);
            var entry = BalanceEntry.Create(
                spaceId, memberId, batch.CounterpartyUserId, row.Amount!.Value, space.Currency,
                batch.EffectiveDate, batch.Kind, row.Note, currentUserId, space.LedgerStartDate, _clock);
            await _balanceEntryRepository.AddAsync(entry, cancellationToken);
            entries.Add(entry);
        }

        space.BumpLedgerVersion();
        await _spaceRepository.UpdateAsync(space, cancellationToken);

        var dtos = await BalanceEntryMapping.ToDtosAsync(entries, currentUserId, _userService, cancellationToken);
        return Result<IReadOnlyList<BalanceEntryDto>>.Success(dtos);
    }
}
