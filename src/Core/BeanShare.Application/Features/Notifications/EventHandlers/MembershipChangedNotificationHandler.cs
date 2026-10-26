using BeanShare.Application.Abstractions;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Events;
using MediatR;

namespace BeanShare.Application.Features.Notifications.EventHandlers;

public sealed class MembershipChangedNotificationHandler(
    INotificationRepository notificationRepository,
    ISpaceRepository spaceRepository,
    IUserRepository userRepository,
    IClock clock) : INotificationHandler<SpaceMembershipChanged>
{
    public async Task Handle(SpaceMembershipChanged notification, CancellationToken cancellationToken)
    {
        var space = await spaceRepository.GetByIdAsync(notification.SpaceId, cancellationToken);
        if (space is null) return;

        var changedUser = await userRepository.GetByIdAsync(notification.UserId, cancellationToken);
        var userName = changedUser?.Name ?? "A user";

        if (notification.ChangeType == MembershipChangeType.MemberJoined)
        {
            foreach (var member in space.Members)
            {
                if (member.UserId == notification.UserId) continue;
                if ((await userRepository.GetAccessStatusAsync(member.UserId, cancellationToken))?.IsPlaceholder == true) continue;

                var n = Notification.Create(
                    member.UserId,
                    NotificationType.MemberJoined,
                    "New Member",
                    $"{userName} joined {space.Name}.",
                    clock,
                    notification.SpaceId);

                await notificationRepository.AddAsync(n, cancellationToken);
            }
        }
        else if (notification.ChangeType == MembershipChangeType.MemberRemoved)
        {
            foreach (var member in space.Members.Where(m => m.Role == SpaceRole.Admin))
            {
                if (member.UserId == notification.UserId) continue;
                if ((await userRepository.GetAccessStatusAsync(member.UserId, cancellationToken))?.IsPlaceholder == true) continue;

                var n = Notification.Create(
                    member.UserId,
                    NotificationType.MemberLeft,
                    "Member Left",
                    $"{userName} is no longer a member of {space.Name}.",
                    clock,
                    notification.SpaceId);

                await notificationRepository.AddAsync(n, cancellationToken);
            }
        }
    }
}
