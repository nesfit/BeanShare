using BeanShare.Application.Features.Settlement.Queries.GetSettlementReportData;
using BeanShare.Domain.ValueObjects;
using BeanShare.Infrastructure.Services.Documents;
using MediatR;

namespace BeanShare.BlazorWeb.Endpoints;

public static class SettlementExportEndpoints
{
    public static IEndpointRouteBuilder MapSettlementExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/settlements").RequireAuthorization();

        group.MapGet("/{id:guid}/export/pdf", ExportPdf);
        group.MapGet("/{id:guid}/export/excel", ExportExcel);

        return endpoints;
    }

    private static async Task<IResult> ExportPdf(
        Guid id,
        IMediator mediator,
        ISettlementReportGenerator reportGenerator)
    {
        var query = new GetSettlementReportDataQuery(new SettlementId(id));
        var result = await mediator.Send(query);

        if (!result.IsSuccess)
        {
            return Results.BadRequest(result.Errors.FirstOrDefault().Message ?? "Failed to get settlement data");
        }

        var pdfBytes = reportGenerator.GeneratePdf(result.Value);
        var fileName = $"Settlement_{result.Value.BillingPeriodName}_{DateTime.UtcNow:yyyyMMdd}.pdf";

        return Results.File(pdfBytes, "application/pdf", fileName);
    }

    private static async Task<IResult> ExportExcel(
        Guid id,
        IMediator mediator,
        ISettlementReportGenerator reportGenerator)
    {
        var query = new GetSettlementReportDataQuery(new SettlementId(id));
        var result = await mediator.Send(query);

        if (!result.IsSuccess)
        {
            return Results.BadRequest(result.Errors.FirstOrDefault().Message ?? "Failed to get settlement data");
        }

        var excelBytes = reportGenerator.GenerateExcel(result.Value);
        var fileName = $"Settlement_{result.Value.BillingPeriodName}_{DateTime.UtcNow:yyyyMMdd}.xlsx";

        return Results.File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
}
