using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Balances.Commands.DeleteBalanceEntry;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Balances;

public sealed class DeleteBalanceEntryRequest
{
    public Guid SpaceId { get; set; }
    public Guid EntryId { get; set; }
}

public sealed class DeleteBalanceEntryEndpoint : Endpoint<DeleteBalanceEntryRequest, EmptyResponse>
{
    private readonly IMediator _mediator;

    public DeleteBalanceEntryEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Delete("/api/spaces/{SpaceId}/balance-entries/{EntryId}");
        Summary(s =>
        {
            s.Summary = "Delete a balance entry";
            s.Description = "Only while no settlement has included it (otherwise ENTRY_LOCKED). Admin only.";
        });
    }

    public override async Task HandleAsync(DeleteBalanceEntryRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteBalanceEntryCommand(req.SpaceId, req.EntryId), ct);

        if (result.IsFailure)
        {
            await this.SendResultErrorsAsync(result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }
}
