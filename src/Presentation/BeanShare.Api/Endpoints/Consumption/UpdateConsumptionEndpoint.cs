using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Consumption.Commands;
using BeanShare.Contracts.Consumption;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Consumption;

public sealed class UpdateConsumptionEndpoint(IMediator mediator)
    : Endpoint<UpdateConsumptionRequest>
{
    public override void Configure()
    {
        Put("/api/consumptions/{Id}");
        Summary(s =>
        {
            s.Summary = "Update a consumption entry";
            s.Description = "Updates a consumption entry. Only allowed for entries in open or draft billing periods.";
        });
    }

    public override async Task HandleAsync(UpdateConsumptionRequest req, CancellationToken ct)
    {
        var command = new UpdateConsumptionCommand(
            req.Id,
            req.SpaceId,
            req.ProductName,
            req.ProductBrand,
            req.ProductType,
            req.QuantityGrams,
            req.ConsumedAt);

        var result = await mediator.Send(command, ct);


        if (await this.TrySendAccessOrConflictAsync(result.Errors, ct))
            return;

        if (result.IsSuccess)
        {
            await SendOkAsync(result.Value, ct);
        }
        else
        {
            foreach (var error in result.Errors)
            {
                AddError(error.Code, error.Message);
            }
            await SendErrorsAsync(cancellation: ct);
        }
    }
}
