using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Admin.Presets.Commands;
using BeanShare.Contracts.Admin;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Admin.Presets;

public sealed class UpdateGlobalPresetEndpoint : Endpoint<UpdateGlobalPresetRequest>
{
    private readonly IMediator _mediator;

    public UpdateGlobalPresetEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Put("/api/admin/presets/{presetId}");
        Roles("admin");
        Summary(s =>
        {
            s.Summary = "Update global preset (Admin)";
            s.Description = "Update an existing global preset";
        });
    }

    public override async Task HandleAsync(UpdateGlobalPresetRequest req, CancellationToken ct)
    {
        var presetId = Route<Guid>("presetId");
        var command = new UpdateGlobalPresetCommand(
            presetId,
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

        await SendNoContentAsync(ct);
    }
}
