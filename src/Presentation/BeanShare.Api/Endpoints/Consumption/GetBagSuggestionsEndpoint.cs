using BeanShare.Application.Features.Consumption.Queries.GetBagSuggestions;
using BeanShare.Domain.Services;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Consumption;

public sealed class GetBagSuggestionsRequest
{
    public Guid SpaceId { get; set; }

    [QueryParam]
    public int WindowSize { get; set; } = BagSuggestionPolicy.DefaultWindowSize;
}

public sealed class GetBagSuggestionsEndpoint(IMediator mediator)
    : Endpoint<GetBagSuggestionsRequest, BagSuggestionsResult>
{
    public override void Configure()
    {
        Get("/api/spaces/{SpaceId}/consumption/bag-suggestions");
        Summary(s =>
        {
            s.Summary = "Rank the space's bags for the log-coffee picker";
            s.Description = "Returns non-empty bags best-first — open bags ahead of sealed ones, and among them "
                + "whichever the space has been pouring from over its last WindowSize cups (the caller's own cups "
                + "count double). The head of the list is flagged as the one to preselect.";
        });
    }

    public override async Task HandleAsync(GetBagSuggestionsRequest req, CancellationToken ct)
    {
        var result = await mediator.Send(new GetBagSuggestionsQuery(req.SpaceId, req.WindowSize), ct);

        if (result.IsSuccess)
        {
            await SendOkAsync(result.Value, ct);
        }
        else
        {
            await SendNotFoundAsync(ct);
        }
    }
}
