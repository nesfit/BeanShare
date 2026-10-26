using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.History.Dtos;
using BeanShare.Application.Features.History.Queries.GetSpaceHistory;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.History;

public sealed class GetSpaceHistoryRequest
{
    public Guid SpaceId { get; init; }
}

public sealed class GetSpaceHistoryEndpoint : Endpoint<GetSpaceHistoryRequest, SpaceHistoryDto>
{
    private readonly IMediator _mediator;

    public GetSpaceHistoryEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Get("/api/spaces/{SpaceId}/history");
        Summary(s =>
        {
            s.Summary = "Get the space's history";
            s.Description = "Imported periods with cups and purchases, all-time cups per member and purchases per year.";
        });
    }

    public override async Task HandleAsync(GetSpaceHistoryRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSpaceHistoryQuery(req.SpaceId), ct);

        if (result.IsFailure)
        {
            await this.SendResultErrorsAsync(result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
