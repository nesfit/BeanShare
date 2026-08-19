using BeanShare.Application.Features.Settlement.Dtos;
using BeanShare.Application.Features.Settlement.Queries.GetUnsettledSummary;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Settlement;

public sealed class GetMyUnsettledSummaryEndpoint : EndpointWithoutRequest<UnsettledSummaryResult>
{
    private readonly IMediator _mediator;

    public GetMyUnsettledSummaryEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Get("/api/me/unsettled");
        Summary(s =>
        {
            s.Summary = "Get what is unbilled for the caller across all their spaces";
            s.Description = "One entry per space: the caller's own cups and cost since that space's " +
                            "last settlement, plus the per-member breakdown for spaces they administer.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetUnsettledSummaryQuery(), ct);

        if (result.IsSuccess)
        {
            await SendOkAsync(result.Value, ct);
            return;
        }

        await SendResultAsync(Results.BadRequest(
            result.Errors.Any() ? result.Errors.First().Message : "Failed to get unsettled summary"));
    }
}
