using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Contracts.CoffeeStock;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.CoffeeStock;

public sealed class SplitRemainingStockEndpoint : Endpoint<SplitRemainingStockRequest, SplitRemainingStockResponse>
{
    private readonly IMediator _mediator;

    public SplitRemainingStockEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{spaceId}/stock/{stockLevelId}/split");
        Summary(s =>
        {
            s.Summary = "Split remaining stock among members";
            s.Description = "Distributes remaining 'phantom' stock (exists in system but not physically) among members proportionally based on their consumption history. Requires admin privileges.";
            s.ExampleRequest = new SplitRemainingStockRequest
            {
                Reason = "Phantom stock adjustment"
            };
        });
    }

    public override async Task HandleAsync(SplitRemainingStockRequest req, CancellationToken ct)
    {
        var spaceId = Route<Guid>("spaceId");
        var stockLevelId = Route<Guid>("stockLevelId");

        var command = new SplitRemainingStockCommand(
            spaceId,
            stockLevelId,
            req.Reason);

        var result = await _mediator.Send(command, ct);

        if (result.IsSuccess)
        {
            var dto = result.Value;
            var response = new SplitRemainingStockResponse
            {
                SpaceId = dto.SpaceId,
                StockLevelId = dto.StockLevelId,
                ProductName = dto.ProductName,
                ProductBrand = dto.ProductBrand,
                ProductType = dto.ProductType,
                TotalDistributedGrams = dto.TotalDistributedGrams,
                Allocations = dto.Allocations.Select(a => new MemberAllocationResponse
                {
                    UserId = a.UserId,
                    UserName = a.UserName,
                    ConsumptionPercentage = a.ConsumptionPercentage,
                    AllocatedGrams = a.AllocatedGrams
                }).ToList(),
                Message = $"Successfully distributed {dto.TotalDistributedGrams}g of {dto.ProductBrand} {dto.ProductName} among {dto.Allocations.Count} members"
            };
            await SendAsync(response, 200, ct);
        }
        else
        {
            if (await this.TrySendAccessOrConflictAsync(result.Errors, ct))
                return;
            foreach (var error in result.Errors)
            {
                AddError(error.Code, error.Message);
            }
            await SendErrorsAsync(cancellation: ct);
        }
    }
}
