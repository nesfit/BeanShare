using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.History.Commands.CommitHistoryImport;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.History;

public sealed class CommitHistoryImportEndpoint : Endpoint<HistoryImportRequest, HistoryImportResultDto>
{
    private readonly IMediator _mediator;

    public CommitHistoryImportEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{SpaceId}/history-imports");
        Summary(s =>
        {
            s.Summary = "Import the Excel history";
            s.Description = "Creates historical settled periods with cup counts and purchases, all or nothing. Admin only.";
        });
    }

    public override async Task HandleAsync(HistoryImportRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CommitHistoryImportCommand(req.SpaceId, req.CupsText, req.PurchasesText, req.Note, req.CreateMissingPlaceholders), ct);

        if (result.IsFailure)
        {
            await this.SendResultErrorsAsync(result, ct);
            return;
        }

        await SendAsync(result.Value, StatusCodes.Status201Created, ct);
    }
}
