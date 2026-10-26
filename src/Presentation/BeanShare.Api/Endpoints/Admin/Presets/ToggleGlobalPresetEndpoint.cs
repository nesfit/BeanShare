using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Admin.Presets.Commands;
using BeanShare.Contracts.Admin;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Admin.Presets;

public sealed class ToggleGlobalPresetEndpoint : Endpoint<ToggleGlobalPresetActiveRequest>
{
    private readonly IMediator _mediator;

    public ToggleGlobalPresetEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/admin/presets/{presetId}/toggle");
        Roles("admin");
        Summary(s =>
        {
            s.Summary = "Toggle global preset active status (Admin)";
            s.Description = "Activate or deactivate a global preset";
        });
    }

    public override async Task HandleAsync(ToggleGlobalPresetActiveRequest req, CancellationToken ct)
    {
        var presetId = Route<Guid>("presetId");
        var command = new ToggleGlobalPresetCommand(presetId, req.Activate);
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
