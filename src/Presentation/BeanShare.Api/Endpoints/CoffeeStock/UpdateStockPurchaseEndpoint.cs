using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Application.Features.CoffeeStock.Dtos;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.CoffeeStock;

public sealed class UpdateStockPurchaseRequest
{
    public Guid SpaceId { get; set; }
    public Guid PurchaseId { get; set; }
    public decimal QuantityGrams { get; set; }
    public decimal CostAmount { get; set; }
    public DateTime PurchasedAt { get; set; }
    public string? ProductName { get; set; }
    public string? ProductBrand { get; set; }
    public string? CoffeeType { get; set; }
    public Guid? PaidByUserId { get; set; }
}

public sealed class UpdateStockPurchaseEndpoint : Endpoint<UpdateStockPurchaseRequest, StockPurchaseDto>
{
    private readonly IMediator _mediator;

    public UpdateStockPurchaseEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Put("/api/spaces/{SpaceId}/stock/purchases/{PurchaseId}");
    }

    public override async Task HandleAsync(UpdateStockPurchaseRequest req, CancellationToken ct)
    {
        var command = new UpdateStockPurchaseCommand(
            req.SpaceId,
            req.PurchaseId,
            req.QuantityGrams,
            req.CostAmount,
            req.PurchasedAt,
            req.ProductName,
            req.ProductBrand,
            req.CoffeeType,
            req.PaidByUserId);

        var result = await _mediator.Send(command, ct);


        if (await this.TrySendConflictAsync(result.Errors, ct))

            return;

        if (result.IsSuccess)
        {
            await SendOkAsync(result.Value, ct);
        }
        else
        {
            await SendResultAsync(Results.BadRequest(result.Errors.First().Message));
        }
    }
}
