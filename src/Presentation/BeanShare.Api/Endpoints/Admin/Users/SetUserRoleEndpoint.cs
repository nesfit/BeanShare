using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Admin.Users.Commands;
using BeanShare.Contracts.Admin;
using BeanShare.Domain.Enums;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Admin.Users;

public sealed class SetUserRoleEndpoint : Endpoint<SetUserRoleRequest>
{
    private readonly IMediator _mediator;

    public SetUserRoleEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Put("/api/admin/users/{userId}/role");
        Roles("admin");
        Summary(s =>
        {
            s.Summary = "Set user system role (Admin)";
            s.Description = "Change a user's system role (User or SystemAdmin)";
        });
    }

    public override async Task HandleAsync(SetUserRoleRequest req, CancellationToken ct)
    {
        var userId = Route<Guid>("userId");

        if (!Enum.TryParse<SystemRole>(req.Role, true, out var role))
        {
            AddError("Role", "Invalid role. Must be 'User' or 'SystemAdmin'");
            await SendErrorsAsync(cancellation: ct);
            return;
        }

        var command = new SetUserRoleCommand(userId, role);
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
