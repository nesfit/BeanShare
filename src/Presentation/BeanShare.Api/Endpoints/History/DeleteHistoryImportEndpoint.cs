using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.History.Commands.DeleteHistoryImport;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.History;

public sealed class DeleteHistoryImportRequest
{
    public Guid SpaceId { get; set; }
    public Guid ImportId { get; set; }
}

public sealed class DeleteHistoryImportEndpoint : Endpoint<DeleteHistoryImportRequest, EmptyResponse>
{
    private readonly IMediator _mediator;

    public DeleteHistoryImportEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Delete("/api/spaces/{SpaceId}/history-imports/{ImportId}");
        Summary(s =>
        {
            s.Summary = "Delete a history import";
            s.Description = "Removes the import with its periods, cup counts and purchases. Admin only.";
        });
    }

    public override async Task HandleAsync(DeleteHistoryImportRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteHistoryImportCommand(req.SpaceId, req.ImportId), ct);

        if (result.IsFailure)
        {
            await this.SendResultErrorsAsync(result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }
}
