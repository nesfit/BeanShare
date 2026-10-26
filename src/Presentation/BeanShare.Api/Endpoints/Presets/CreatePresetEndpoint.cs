using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Presets.Commands;
using BeanShare.Contracts.Presets;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Presets;

public sealed class CreatePresetEndpoint : Endpoint<CreatePresetRequest, CreatePresetResponse>
{
    private readonly IMediator _mediator;

    public CreatePresetEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/presets");
        Summary(s =>
        {
            s.Summary = "Create a new preset recipe";
            s.Description = "Creates a new coffee preset recipe for quick consumption logging";
            s.ExampleRequest = new CreatePresetRequest
            {
                SpaceId = Guid.NewGuid(),
                Name = "Morning Espresso",
                CoffeeType = "Arabica",
                Preparation = "Espresso",
                DefaultGrams = 18,
                Notes = "Double shot, 1:2 ratio",
                IsShared = true
            };
        });
    }

    public override async Task HandleAsync(CreatePresetRequest req, CancellationToken ct)
    {
        var command = new CreatePresetCommand(
            req.SpaceId,
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

        var response = new CreatePresetResponse
        {
            PresetId = result.Value.PresetId,
            Message = $"Preset '{req.Name}' created successfully"
        };

        await SendCreatedAtAsync("GetPresetById", new { id = result.Value.PresetId }, response, cancellation: ct);
    }
}