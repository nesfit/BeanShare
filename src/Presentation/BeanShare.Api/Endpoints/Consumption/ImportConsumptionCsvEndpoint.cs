using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Consumption.Commands;
using BeanShare.Application.Features.Consumption.Dtos;
using BeanShare.Contracts.Consumption;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Consumption;

public sealed class ImportConsumptionCsvEndpoint(IMediator mediator)
    : Endpoint<ImportConsumptionCsvRequest, ImportConsumptionCsvResult>
{
    public override void Configure()
    {
        Post("/api/consumptions/import-csv");
        Summary(s =>
        {
            s.Summary = "Bulk import consumption entries from CSV data";
            s.Description = "Imports consumption entries in bulk. Requires space admin privileges. Deducts stock when matching products are found.";
        });
    }

    public override async Task HandleAsync(ImportConsumptionCsvRequest req, CancellationToken ct)
    {
        var rows = req.Rows.Select(r => new ImportConsumptionCsvRow(
            r.RowNumber,
            r.Email,
            r.ProductName,
            r.ProductBrand,
            r.ProductType,
            r.QuantityGrams,
            r.ConsumedAt
        )).ToList();

        var command = new ImportConsumptionCsvCommand(req.SpaceId, rows);
        var result = await mediator.Send(command, ct);

        if (await this.TrySendConflictAsync(result.Errors, ct))
            return;

        if (result.IsSuccess)
        {
            await SendAsync(result.Value, 200, ct);
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
