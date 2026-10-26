using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Notifications.Commands.MarkAllAsRead;
using BeanShare.Contracts.Notifications;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Notifications;

public sealed class MarkAllAsReadEndpoint : EndpointWithoutRequest<MarkAsReadResponse>
{
    private readonly IMediator _mediator;

    public MarkAllAsReadEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/me/notifications/read-all");
        Summary(s =>
        {
            s.Summary = "Mark all notifications as read";
            s.Description = "Mark all notifications for the current user as read";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var command = new MarkAllAsReadCommand();
        var result = await _mediator.Send(command, ct);

        if (!result.IsSuccess)
        {
            if (await this.TrySendAccessOrConflictAsync(result.Errors, ct))
                return;
            AddError("Notifications", result.Errors.Any() ? result.Errors.First().Message : "Failed to mark all as read");
            await SendErrorsAsync(400, ct);
            return;
        }

        await SendOkAsync(new MarkAsReadResponse(result.Value), ct);
    }
}
