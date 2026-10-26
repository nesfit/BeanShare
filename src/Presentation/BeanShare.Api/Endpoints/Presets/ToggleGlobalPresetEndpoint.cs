using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Presets.Commands;
using BeanShare.Contracts.Presets;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Presets;

public sealed class ToggleGlobalPresetEndpoint : Endpoint<ToggleGlobalPresetRequest>
{
    private readonly IMediator _mediator;

    public ToggleGlobalPresetEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Put("/api/spaces/{spaceId}/global-presets/{globalPresetId}/toggle");
        Summary(s =>
        {
            s.Summary = "Toggle a global preset for a space";
            s.Description = "Enable or disable a global preset for a specific space (admin only)";
        });
    }

    public override async Task HandleAsync(ToggleGlobalPresetRequest req, CancellationToken ct)
    {
        var spaceId = Route<Guid>("spaceId");
        var globalPresetId = Route<Guid>("globalPresetId");

        var command = new ToggleGlobalPresetCommand(spaceId, globalPresetId, req.IsEnabled);
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

        await SendOkAsync(ct);
    }
}
