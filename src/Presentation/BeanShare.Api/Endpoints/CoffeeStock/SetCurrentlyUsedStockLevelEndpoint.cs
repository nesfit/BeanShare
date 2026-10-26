using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.CoffeeStock.Commands;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.CoffeeStock;

public sealed class SetCurrentlyUsedStockLevelEndpoint : EndpointWithoutRequest
{
    private readonly IMediator _mediator;

    public SetCurrentlyUsedStockLevelEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{spaceId}/stock/{stockLevelId}/currently-used");
        Summary(s =>
        {
            s.Summary = "Set currently used coffee";
            s.Description = "Marks a stock level as the currently used coffee for the space. Clears the flag from any previously selected stock level. Requires admin privileges.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var spaceId = Route<Guid>("spaceId");
        var stockLevelId = Route<Guid>("stockLevelId");

        var command = new SetCurrentlyUsedStockLevelCommand(spaceId, stockLevelId);
        var result = await _mediator.Send(command, ct);

        if (result.IsSuccess)
        {
            await SendOkAsync(new { Message = "Currently used coffee set successfully" }, ct);
        }
        else
        {
            if (await this.TrySendConflictAsync(result.Errors, ct))
                return;
            foreach (var error in result.Errors)
            {
                AddError(error.Code, error.Message);
            }
            await SendErrorsAsync(cancellation: ct);
        }
    }
}
