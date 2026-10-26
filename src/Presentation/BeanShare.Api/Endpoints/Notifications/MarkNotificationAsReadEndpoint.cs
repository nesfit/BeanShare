using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Notifications.Commands.MarkNotificationAsRead;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Notifications;

public sealed class MarkNotificationAsReadRequest
{
    public Guid NotificationId { get; set; }
}

public sealed class MarkNotificationAsReadEndpoint : Endpoint<MarkNotificationAsReadRequest>
{
    private readonly IMediator _mediator;

    public MarkNotificationAsReadEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/me/notifications/{NotificationId}/read");
        Summary(s =>
        {
            s.Summary = "Mark notification as read";
            s.Description = "Mark a specific notification as read";
        });
    }

    public override async Task HandleAsync(MarkNotificationAsReadRequest req, CancellationToken ct)
    {
        var command = new MarkNotificationAsReadCommand(req.NotificationId);
        var result = await _mediator.Send(command, ct);

        if (!result.IsSuccess)
        {
            if (await this.TrySendAccessOrConflictAsync(result.Errors, ct))
                return;
            var error = result.Errors.First();
            if (error.Code == "NOTIFICATION_NOT_FOUND")
            {
                await SendNotFoundAsync(ct);
                return;
            }
            AddError("Notification", error.Message);
            await SendErrorsAsync(400, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }
}
