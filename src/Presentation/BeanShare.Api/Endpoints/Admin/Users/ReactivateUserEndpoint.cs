using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Admin.Users.Commands;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Admin.Users;

public sealed class ReactivateUserEndpoint : EndpointWithoutRequest
{
    private readonly IMediator _mediator;

    public ReactivateUserEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/admin/users/{userId}/reactivate");
        Roles("admin");
        Summary(s =>
        {
            s.Summary = "Reactivate user (Admin)";
            s.Description = "Reactivate a deactivated user account";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Route<Guid>("userId");
        var command = new ReactivateUserCommand(userId);
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
