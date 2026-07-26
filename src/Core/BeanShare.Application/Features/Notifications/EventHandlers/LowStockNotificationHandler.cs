using BeanShare.Application.Abstractions;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Events;
using MediatR;

namespace BeanShare.Application.Features.Notifications.EventHandlers;

/// <summary>
/// After each consumption, forecasts when the product will run out from its recent velocity and
/// notifies space admins when it will run dry within the delivery lead time — early enough to batch
/// order (with a hard low-grams floor as a backstop). De-duplicates against an existing unread
/// low-stock alert for the same space, so admins aren't pinged on every cup.
/// </summary>
public sealed class LowStockNotificationHandler(
    INotificationRepository notificationRepository,
    ISpaceRepository spaceRepository,
    IConsumptionRepository consumptionRepository,
    IClock clock) : INotificationHandler<StockConsumed>
{
    private const int LeadTimeDays = 7;
    private const int ForecastWindowDays = 30;
    private const decimal HardFloorGrams = 50m;

    public async Task Handle(StockConsumed notification, CancellationToken cancellationToken)
    {
        var remaining = notification.RemainingStock.Grams;

        // Velocity (g/day) for THIS product over the recent window → days until it runs out.
        var windowStart = clock.UtcNow.AddDays(-ForecastWindowDays);
        var cups = await consumptionRepository.GetBySpaceIdAsync(notification.SpaceId, cancellationToken);
        var consumed = cups
            .Where(c => c.ConsumedAt >= windowStart
                && string.Equals(c.Product.Name, notification.Product.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.Product.Brand, notification.Product.Brand, StringComparison.OrdinalIgnoreCase)
                && c.Product.Type == notification.Product.Type)
            .Sum(c => c.Quantity.Grams);

        var velocity = (double)consumed / ForecastWindowDays;
        int? daysToEmpty = velocity > 0.01 ? (int)Math.Floor((double)remaining / velocity) : null;

        var predictedSoon = daysToEmpty.HasValue && daysToEmpty.Value <= LeadTimeDays;
        var belowFloor = remaining <= HardFloorGrams;
        if (!predictedSoon && !belowFloor)
        {
            return;
        }

        var space = await spaceRepository.GetByIdAsync(notification.SpaceId, cancellationToken);
        if (space is null)
        {
            return;
        }

        var message = daysToEmpty.HasValue
            ? $"{notification.Product.Name} ({notification.Product.Brand}) in {space.Name} will run out in ~{Math.Max(0, daysToEmpty.Value)} day(s) — order now so it arrives in time ({remaining:0.#} g left)."
            : $"{notification.Product.Name} ({notification.Product.Brand}) is running low in {space.Name} — only {remaining:0.#} g remaining.";

        foreach (var admin in space.Members.Where(m => m.Role == SpaceRole.Admin))
        {
            // One open low-stock alert per space per admin — cleared when they read it.
            var unread = await notificationRepository.GetUnreadByUserIdAsync(admin.UserId, cancellationToken);
            if (unread.Any(x => x.Type == NotificationType.LowStock && x.SpaceId == notification.SpaceId))
            {
                continue;
            }

            var n = Notification.Create(
                admin.UserId,
                NotificationType.LowStock,
                "Restock soon",
                message,
                clock,
                notification.SpaceId);

            await notificationRepository.AddAsync(n, cancellationToken);
        }
    }
}
