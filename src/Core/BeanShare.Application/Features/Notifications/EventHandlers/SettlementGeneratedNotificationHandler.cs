using BeanShare.Application.Abstractions;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Events;
using BeanShare.Domain.Specifications;
using MediatR;

namespace BeanShare.Application.Features.Notifications.EventHandlers;

public sealed class SettlementGeneratedNotificationHandler(
    INotificationRepository notificationRepository,
    ISpaceRepository spaceRepository,
    IClock clock,
    BeanShare.Application.Services.IUserService userService) : INotificationHandler<SettlementGenerated>
{
    public async Task Handle(SettlementGenerated notification, CancellationToken cancellationToken)
    {
        var space = await spaceRepository.GetByIdAsync(notification.SpaceId, cancellationToken);
        if (space is null) return;

        // Placeholders cannot sign in, so they get no notifications.
        var placeholders = (await userService.GetByIdsAsync(space.Members.Select(m => m.UserId), cancellationToken))
            .Where(u => u.IsPlaceholder)
            .Select(u => u.Id)
            .ToHashSet();

        foreach (var member in space.Members)
        {
            if (placeholders.Contains(member.UserId)) continue;
            if (member.UserId == notification.GeneratedBy) continue;

            var n = Notification.Create(
                member.UserId,
                NotificationType.SettlementReady,
                "Settlement Ready",
                $"A new settlement has been generated for {space.Name}. Please review your share.",
                clock,
                notification.SpaceId,
                actionUrl: $"/spaces/{notification.SpaceId.Value}/settlements/{notification.SettlementId.Value}");

            await notificationRepository.AddAsync(n, cancellationToken);
        }
    }
}
