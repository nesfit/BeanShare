using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Presets.Commands;
using BeanShare.Contracts.Presets;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Presets;

public sealed class TogglePresetFavoriteEndpoint : Endpoint<TogglePresetFavoriteRequest>
{
    private readonly IMediator _mediator;

    public TogglePresetFavoriteEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{spaceId}/preset-favorites/toggle");
        Summary(s =>
        {
            s.Summary = "Toggle a preset as favorite";
            s.Description = "Add or remove a preset from the user's favorites for a specific space";
        });
    }

    public override async Task HandleAsync(TogglePresetFavoriteRequest req, CancellationToken ct)
    {
        var spaceId = Route<Guid>("spaceId");

        var command = new TogglePresetFavoriteCommand(
            spaceId,
            req.GlobalPresetId,
            req.SpacePresetId,
            req.IsFavorite);
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
