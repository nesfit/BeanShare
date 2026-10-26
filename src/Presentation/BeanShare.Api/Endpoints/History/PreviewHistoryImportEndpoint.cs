using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.History.Dtos;
using BeanShare.Application.Features.History.Queries.PreviewHistoryImport;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.History;

/// <summary>The two Excel history blocks: <c>period;start;end;member;cups</c> and <c>period;date;paidBy;description;grams;cost</c>.</summary>
public sealed class HistoryImportRequest
{
    public Guid SpaceId { get; set; }
    public string CupsText { get; set; } = "";
    public string? PurchasesText { get; set; }
    public string? Note { get; set; }
    public bool CreateMissingPlaceholders { get; set; }
}

public sealed class PreviewHistoryImportEndpoint : Endpoint<HistoryImportRequest, HistoryImportPreviewDto>
{
    private readonly IMediator _mediator;

    public PreviewHistoryImportEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{SpaceId}/history-imports/preview");
        Summary(s =>
        {
            s.Summary = "Preview an Excel history import";
            s.Description = "Parses both blocks and reports periods, totals, errors and members to be created. Admin only.";
        });
    }

    public override async Task HandleAsync(HistoryImportRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new PreviewHistoryImportQuery(req.SpaceId, req.CupsText, req.PurchasesText, req.CreateMissingPlaceholders), ct);

        if (result.IsFailure)
        {
            await this.SendResultErrorsAsync(result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
