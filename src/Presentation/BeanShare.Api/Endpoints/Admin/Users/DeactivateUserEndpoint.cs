using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Admin.Users.Commands;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Admin.Users;

public sealed class DeactivateUserEndpoint : EndpointWithoutRequest
{
    private readonly IMediator _mediator;

    public DeactivateUserEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Delete("/api/admin/users/{userId}");
        Roles("admin");
        Summary(s =>
        {
            s.Summary = "Deactivate user (Admin)";
            s.Description = "Deactivate a user account";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Route<Guid>("userId");
        var command = new DeactivateUserCommand(userId);
        var result = await _mediator.Send(command, ct);

        if (result.IsFailure)
        {
            if (await this.TrySendAccessOrConflictAsync(result.Errors, ct))
                return;
            foreach (var error in result.Errors)
            {
                AddError(error.Code, error.Message);
            }
            await SendErrorsAsync(cancellation: ct);
            return;
        }

        await SendNoContentAsync(ct);
    }
}
