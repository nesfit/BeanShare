using BeanShare.Api.Endpoints.Common;
using BeanShare.Api.Endpoints.Consumption.Validators;
using BeanShare.Application.Features.Consumption.Commands;
using BeanShare.Contracts.Consumption;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Consumption;

public sealed class RecordConsumptionEndpoint(IMediator mediator)
    : Endpoint<RecordConsumptionRequest, RecordConsumptionResponse>
{
    public override void Configure()
    {
        Post("/api/consumptions");
        Validator<RecordConsumptionRequestValidator>();
        Summary(s =>
        {
            s.Summary = "Record coffee consumption";
            s.Description = "Records coffee consumption from stock. Deducts from inventory and creates consumption entry.";
            s.ExampleRequest = new RecordConsumptionRequest
            {
                SpaceId = Guid.NewGuid(),
                ProductName = "Premium Espresso Blend",
                ProductBrand = "Blue Mountain Coffee",
                ProductType = "Espresso",
                QuantityGrams = 15.0m,
                ConsumedAt = DateTime.UtcNow.AddMinutes(-30)
            };
        });
    }

    public override async Task HandleAsync(RecordConsumptionRequest req, CancellationToken ct)
    {
        var command = new RecordConsumptionCommand(
            req.SpaceId,
            req.ProductName,
            req.ProductBrand,
            req.ProductType,
            req.QuantityGrams,
            req.ConsumedAt,
            req.PresetName,
            req.ForUserId,
            req.StockLotId);

        var result = await mediator.Send(command, ct);


        if (await this.TrySendConflictAsync(result.Errors, ct))
            return;

        if (result.IsSuccess)
        {
            var consumption = result.Value;
            var response = new RecordConsumptionResponse
            {
                SpaceId = consumption.SpaceId,
                ProductName = consumption.ProductName,
                ProductBrand = consumption.ProductBrand,
                ProductType = consumption.ProductType,
                ConsumedGrams = consumption.QuantityGrams,
                RemainingGrams = consumption.RemainingGrams,
                ConsumedAt = consumption.ConsumedAt,
                CostAmount = consumption.CostAmount,
                CostCurrency = consumption.CostCurrency
            };
            await SendAsync(response, 201, ct);
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