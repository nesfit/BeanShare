using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Admin.Presets.Commands;
using BeanShare.Contracts.Admin;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Admin.Presets;

public sealed class CreateGlobalPresetEndpoint : Endpoint<CreateGlobalPresetRequest, CreateGlobalPresetResponse>
{
    private readonly IMediator _mediator;

    public CreateGlobalPresetEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/admin/presets");
        Roles("admin");
        Summary(s =>
        {
            s.Summary = "Create global preset (Admin)";
            s.Description = "Create a new global preset available to all spaces";
        });
    }

    public override async Task HandleAsync(CreateGlobalPresetRequest req, CancellationToken ct)
    {
        var command = new CreateGlobalPresetCommand(
            req.Name,
            req.DefaultCoffeeType,
            req.DefaultPreparation,
            req.DefaultGrams,
            req.Description,
            req.DisplayOrder);

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

        var response = new CreateGlobalPresetResponse { Id = result.Value };
        await SendCreatedAtAsync<GetAllGlobalPresetsEndpoint>(null, response, cancellation: ct);
    }
}
