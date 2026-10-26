using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Balances.Queries.GetPhantomCandidates;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Balances;

public sealed class GetPhantomCandidatesRequest
{
    public Guid SpaceId { get; init; }

    [QueryParam]
    public bool IncludeAllBagLess { get; init; }
}

public sealed class GetPhantomCandidatesEndpoint : Endpoint<GetPhantomCandidatesRequest, PhantomCandidatesDto>
{
    private readonly IMediator _mediator;

    public GetPhantomCandidatesEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Get("/api/spaces/{SpaceId}/balances/cutover/phantom-candidates");
        Summary(s =>
        {
            s.Summary = "List rows the cutover may void";
            s.Description = "Write-off shares and split-like bag-less rows since the ledger start, grouped into events; includeAllBagLess adds every bag-less row. Admin only.";
        });
    }

    public override async Task HandleAsync(GetPhantomCandidatesRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPhantomCandidatesQuery(req.SpaceId, req.IncludeAllBagLess), ct);

        if (result.IsFailure)
        {
            await this.SendResultErrorsAsync(result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
