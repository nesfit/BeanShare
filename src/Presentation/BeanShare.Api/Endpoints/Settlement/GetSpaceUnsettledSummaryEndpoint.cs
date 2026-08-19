using BeanShare.Application.Features.Settlement.Dtos;
using BeanShare.Application.Features.Settlement.Queries.GetUnsettledSummary;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Settlement;

public sealed class GetSpaceUnsettledSummaryRequest
{
    public Guid SpaceId { get; init; }
}

public sealed class GetSpaceUnsettledSummaryEndpoint
    : Endpoint<GetSpaceUnsettledSummaryRequest, UnsettledSpaceSummaryDto>
{
    private readonly IMediator _mediator;

    public GetSpaceUnsettledSummaryEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Get("/api/spaces/{SpaceId}/unsettled");
        Summary(s =>
        {
            s.Summary = "Get what is unbilled in a space since its last settlement";
            s.Description = "Cups and cost run up after the most recent settled billing period. " +
                            "Space admins also get the per-member breakdown.";
        });
    }

    public override async Task HandleAsync(GetSpaceUnsettledSummaryRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetUnsettledSummaryQuery(req.SpaceId), ct);

        if (result.IsSuccess && result.Value.Spaces.Count > 0)
        {
            await SendOkAsync(result.Value.Spaces[0], ct);
            return;
        }

        await SendResultAsync(Results.BadRequest(
            result.Errors.Any() ? result.Errors.First().Message : "Failed to get unsettled summary"));
    }
}
