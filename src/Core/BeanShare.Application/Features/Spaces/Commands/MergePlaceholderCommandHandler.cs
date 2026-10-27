using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BeanShare.Application.Features.Spaces.Commands;

public sealed class MergePlaceholderCommandHandler : IRequestHandler<MergePlaceholderCommand, Result>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUserService _userService;
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly IHistoryImportRepository _historyImportRepository;
    private readonly ISettlementRepository _settlementRepository;
    private readonly INotificationRepository _notificationRepository;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly ILogger<MergePlaceholderCommandHandler> _logger;

    public MergePlaceholderCommandHandler(
        ISpaceRepository spaceRepository,
        IUserRepository userRepository,
        IUserService userService,
        ICoffeeStockRepository coffeeStockRepository,
        IHistoryImportRepository historyImportRepository,
        ISettlementRepository settlementRepository,
        INotificationRepository notificationRepository,
        IUserContext userContext,
        IClock clock,
        ILogger<MergePlaceholderCommandHandler> logger)
    {
        _spaceRepository = spaceRepository;
        _userRepository = userRepository;
        _userService = userService;
        _coffeeStockRepository = coffeeStockRepository;
        _historyImportRepository = historyImportRepository;
        _settlementRepository = settlementRepository;
        _notificationRepository = notificationRepository;
        _userContext = userContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result> Handle(MergePlaceholderCommand command, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(command.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(spaceId), cancellationToken);
        if (space is null)
            return Result.Failure(Error.SpaceNotFound(command.SpaceId));

        var placeholderId = new UserId(command.PlaceholderUserId);
        var targetId = new UserId(command.TargetUserId);
        if (!space.HasMember(placeholderId))
            return Result.Failure(Error.MemberNotFound(command.PlaceholderUserId, command.SpaceId));

        // Both sides are read uncached: a claim, a merge or a deactivation elsewhere counts at once.
        var placeholderStatus = await _userRepository.GetAccessStatusAsync(placeholderId, cancellationToken);
        if (placeholderStatus is null || !placeholderStatus.Exists || !placeholderStatus.IsPlaceholder)
            return Result.Failure(Error.NotPlaceholder());
        if (placeholderStatus.IsMerged)
            return Result.Failure(Error.MemberMerged());

        var targetStatus = await _userRepository.GetAccessStatusAsync(targetId, cancellationToken);
        if (!space.HasMember(targetId) || targetStatus is null || !targetStatus.Exists || targetStatus.IsPlaceholder || targetStatus.IsMerged)
            return Result.Failure(Error.ValidationFailure("Target", "Merge into a current member who has an account"));

        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        var bags = coffeeStock?.Purchases.Where(p => p.EffectivePayer == placeholderId).ToList() ?? [];
        if (bags.Any(p => !p.IsEmpty && p.RemainingGrams.IsPositive))
            return Result.Failure(Error.MemberHasBalance("This placeholder paid for a bag that still holds coffee; change who paid for it first"));

        var placeholder = await _userRepository.GetByIdAsync(placeholderId, cancellationToken);
        var target = await _userRepository.GetByIdAsync(targetId, cancellationToken);
        if (placeholder is null || target is null)
            return Result.Failure(Error.UserNotFound(placeholder is null ? command.PlaceholderUserId : command.TargetUserId));

        // A claim or another merge may have committed since the uncached reads above: refuse as they would have, rather
        // than let the domain throw on the changed user.
        if (placeholder.IsMerged)
            return Result.Failure(Error.MemberMerged());
        if (!placeholder.IsPlaceholder)
            return Result.Failure(Error.NotPlaceholder());
        if (target.IsPlaceholder || target.IsMerged)
            return Result.Failure(Error.ValidationFailure("Target", "Merge into a current member who has an account"));

        var releasedEmail = placeholder.Email;
        placeholder.MarkMergedInto(targetId);
        placeholder.ReleaseEmail();

        // Close the revival path at its source: emptied bags it paid for now name the target.
        foreach (var bag in bags)
            coffeeStock!.SetPurchasePaidBy(bag.Id, targetId, _clock);
        if (coffeeStock is not null && bags.Count > 0)
            await _coffeeStockRepository.UpdateAsync(coffeeStock, cancellationToken);

        foreach (var import in await _historyImportRepository.GetBySpaceIdAsync(spaceId, cancellationToken))
            import.MoveMember(placeholderId, targetId);

        // A racing confirm or generation now conflicts, so the debt can never move twice.
        space.BumpLedgerVersion();
        await _spaceRepository.UpdateAsync(space, cancellationToken);
        if (await _settlementRepository.GetLatestLedgerForUpdateAsync(spaceId, cancellationToken) is { } latest)
        {
            latest.BumpVersion();
            await _settlementRepository.UpdateAsync(latest, cancellationToken);
        }

        foreach (var admin in space.Members.Where(m => m.Role == SpaceRole.Admin))
        {
            await _notificationRepository.AddAsync(
                Notification.Create(
                    admin.UserId,
                    NotificationType.SystemAnnouncement,
                    "Placeholder merged",
                    $"Placeholder {placeholder.Name} was merged into {target.Name} in {space.Name}; its balance now belongs to {target.Name}.",
                    _clock,
                    spaceId),
                cancellationToken);
        }

        _userService.Evict(placeholderId, releasedEmail, placeholder.Email);
        _logger.LogInformation(
            "Placeholder {PlaceholderId} merged into {TargetId} in space {SpaceId} by {AdminId}",
            placeholderId, targetId, spaceId, _userContext.CurrentUserId);

        return Result.Success();
    }
}
