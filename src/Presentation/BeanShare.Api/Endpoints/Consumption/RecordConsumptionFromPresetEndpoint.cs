using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Consumption.Commands;
using BeanShare.Application.Features.Consumption.Dtos;
using BeanShare.Contracts.Consumption;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Consumption;

public sealed class RecordConsumptionFromPresetEndpoint : Endpoint<RecordConsumptionFromPresetRequest, ConsumptionEntryDto>
{
    private readonly IMediator _mediator;

    public RecordConsumptionFromPresetEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{SpaceId}/consumption/preset");
        Summary(s =>
        {
            s.Summary = "Record coffee consumption using a preset recipe";
            s.Description = "Records a new coffee consumption entry using predefined values from a preset recipe";
            s.ExampleRequest = new RecordConsumptionFromPresetRequest
            {
                PresetId = Guid.NewGuid(),
                CustomQuantityGrams = 20,
                ConsumedAt = DateTime.UtcNow
            };
        });
    }

    public override async Task HandleAsync(RecordConsumptionFromPresetRequest req, CancellationToken ct)
    {
        var spaceId = Route<Guid>("SpaceId");

        var command = new RecordConsumptionFromPresetCommand(
            spaceId,
            req.PresetId,
            req.CustomQuantityGrams,
            req.ConsumedAt,
            req.ForUserId);

        var result = await _mediator.Send(command, ct);


        if (await this.TrySendConflictAsync(result.Errors, ct))

            return;

        if (result.IsFailure)
        {
            foreach (var error in result.Errors)
            {
                AddError(error.Code, error.Message);
            }

            await SendErrorsAsync(cancellation: ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}