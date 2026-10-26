using BeanShare.Application.Abstractions;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Events;
using MediatR;

namespace BeanShare.Application.Features.Notifications.EventHandlers;

public sealed class BillingPeriodClosedNotificationHandler(
    INotificationRepository notificationRepository,
    ISpaceRepository spaceRepository,
    IClock clock,
    BeanShare.Application.Services.IUserService userService) : INotificationHandler<BillingPeriodClosed>
{
    public async Task Handle(BillingPeriodClosed notification, CancellationToken cancellationToken)
    {
        var space = await spaceRepository.GetByIdAsync(notification.SpaceId, cancellationToken);
        if (space is null) return;

        // Placeholders cannot sign in, so they get no notifications (DS-33).
        var placeholders = (await userService.GetByIdsAsync(space.Members.Select(m => m.UserId), cancellationToken))
            .Where(u => u.IsPlaceholder)
            .Select(u => u.Id)
            .ToHashSet();

        foreach (var member in space.Members)
        {
            if (placeholders.Contains(member.UserId)) continue;
            if (member.UserId == notification.ClosedBy) continue;

            var n = Notification.Create(
                member.UserId,
                NotificationType.BillingPeriodClosed,
                "Billing Period Closed",
                $"A billing period in {space.Name} has been closed. A settlement will follow.",
                clock,
                notification.SpaceId);

            await notificationRepository.AddAsync(n, cancellationToken);
        }
    }
}
