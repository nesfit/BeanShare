using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Presets.Commands;
using BeanShare.Contracts.Presets;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Presets;

public sealed class RecordPresetUsageEndpoint : EndpointWithoutRequest<RecordPresetUsageResponse>
{
    private readonly IMediator _mediator;

    public RecordPresetUsageEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/presets/{id}/use");
        Summary(s =>
        {
            s.Summary = "Record usage of a preset";
            s.Description = "Records that a preset recipe has been used (increments usage count and updates last used date)";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var presetId = Route<Guid>("id");

        var command = new RecordPresetUsageCommand(presetId);
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

        var response = new RecordPresetUsageResponse
        {
            Message = "Preset usage recorded successfully"
        };

        await SendOkAsync(response, ct);
    }
}