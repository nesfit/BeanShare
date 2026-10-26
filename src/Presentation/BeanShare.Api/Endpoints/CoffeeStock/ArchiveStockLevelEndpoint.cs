using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.CoffeeStock.Commands;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.CoffeeStock;

public sealed class ArchiveStockLevelEndpoint : EndpointWithoutRequest
{
    private readonly IMediator _mediator;

    public ArchiveStockLevelEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{spaceId}/stock/{stockLevelId}/archive");
        Summary(s =>
        {
            s.Summary = "Archive a stock level";
            s.Description = "Archives a fully consumed stock level so it no longer appears in the active stock list. Requires admin privileges.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var spaceId = Route<Guid>("spaceId");
        var stockLevelId = Route<Guid>("stockLevelId");

        var command = new ArchiveStockLevelCommand(spaceId, stockLevelId);
        var result = await _mediator.Send(command, ct);

        if (result.IsSuccess)
        {
            await SendOkAsync(new { Message = "Stock level archived successfully" }, ct);
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
