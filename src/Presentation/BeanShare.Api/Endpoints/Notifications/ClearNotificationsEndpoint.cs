using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Notifications.Commands.ClearNotifications;
using BeanShare.Contracts.Notifications;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Notifications;

public sealed class ClearNotificationsEndpoint : EndpointWithoutRequest<ClearNotificationsResponse>
{
    private readonly IMediator _mediator;

    public ClearNotificationsEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Delete("/api/me/notifications");
        Summary(s =>
        {
            s.Summary = "Clear all notifications";
            s.Description = "Delete all notifications for the current user";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var command = new ClearNotificationsCommand();
        var result = await _mediator.Send(command, ct);

        if (!result.IsSuccess)
        {
            if (await this.TrySendAccessOrConflictAsync(result.Errors, ct))
                return;
            AddError("Notifications", result.Errors.Any() ? result.Errors.First().Message : "Failed to clear notifications");
            await SendErrorsAsync(400, ct);
            return;
        }

        await SendOkAsync(new ClearNotificationsResponse(result.Value), ct);
    }
}
