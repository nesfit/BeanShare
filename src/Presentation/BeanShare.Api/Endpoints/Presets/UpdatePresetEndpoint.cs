using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Presets.Commands;
using BeanShare.Contracts.Presets;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Presets;

public sealed class UpdatePresetEndpoint : Endpoint<UpdatePresetRequest, UpdatePresetResponse>
{
    private readonly IMediator _mediator;

    public UpdatePresetEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Put("/api/presets/{id}");
        Summary(s =>
        {
            s.Summary = "Update an existing preset recipe";
            s.Description = "Updates an existing coffee preset recipe";
            s.ExampleRequest = new UpdatePresetRequest
            {
                Name = "Morning Espresso",
                CoffeeType = "Arabica",
                Preparation = "Espresso",
                DefaultGrams = 20,
                Notes = "Updated: Double shot, 1:2.5 ratio",
                IsShared = true
            };
        });
    }

    public override async Task HandleAsync(UpdatePresetRequest req, CancellationToken ct)
    {
        var presetId = Route<Guid>("id");

        var command = new UpdatePresetCommand(
            presetId,
            req.Name,
            req.CoffeeType,
            req.Preparation,
            req.DefaultGrams,
            req.Notes,
            req.IsShared);

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

        var response = new UpdatePresetResponse
        {
            Message = "Preset updated successfully"
        };

        await SendOkAsync(response, ct);
    }
}