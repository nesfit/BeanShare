using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.CoffeeStock.Commands.SetBagPaidInExcel;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.CoffeeStock;

public sealed class SetBagPaidInExcelRequest
{
    public Guid SpaceId { get; set; }
    public Guid PurchaseId { get; set; }
    public bool PaidInExcel { get; set; }
}

public sealed class SetBagPaidInExcelEndpoint : Endpoint<SetBagPaidInExcelRequest, EmptyResponse>
{
    private readonly IMediator _mediator;

    public SetBagPaidInExcelEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Put("/api/spaces/{SpaceId}/stock/purchases/{PurchaseId}/paid-in-excel");
        Summary(s =>
        {
            s.Summary = "Mark a bag as paid in Excel";
            s.Description = "A bag bought before the ledger start that the spreadsheet already settled earns no ledger credit. Admin only; refused after the first ledger settlement.";
        });
    }

    public override async Task HandleAsync(SetBagPaidInExcelRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new SetBagPaidInExcelCommand(req.SpaceId, req.PurchaseId, req.PaidInExcel), ct);

        if (result.IsFailure)
        {
            await this.SendResultErrorsAsync(result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }
}
