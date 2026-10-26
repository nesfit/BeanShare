using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Presets.Commands;
using BeanShare.Contracts.Presets;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Presets;

public sealed class DeletePresetEndpoint : EndpointWithoutRequest<DeletePresetResponse>
{
    private readonly IMediator _mediator;

    public DeletePresetEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Delete("/api/presets/{id}");
        Summary(s =>
        {
            s.Summary = "Delete a preset recipe";
            s.Description = "Deletes an existing coffee preset recipe";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var presetId = Route<Guid>("id");

        var command = new DeletePresetCommand(presetId);
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

        var response = new DeletePresetResponse
        {
            Message = "Preset deleted successfully"
        };

        await SendOkAsync(response, ct);
    }
}