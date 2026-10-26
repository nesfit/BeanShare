using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Balances.Dtos;
using BeanShare.Application.Features.Balances.Queries.GetSpaceBalances;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Balances;

public sealed class GetSpaceBalancesRequest
{
    public Guid SpaceId { get; init; }
}

public sealed class GetSpaceBalancesEndpoint : Endpoint<GetSpaceBalancesRequest, SpaceBalancesDto>
{
    private readonly IMediator _mediator;

    public GetSpaceBalancesEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Get("/api/spaces/{SpaceId}/balances");
        Summary(s =>
        {
            s.Summary = "Get the money ledger of a space";
            s.Description = "Ledger settings, running balances and balance entries. Admins see every member; members see their own.";
        });
    }

    public override async Task HandleAsync(GetSpaceBalancesRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSpaceBalancesQuery(req.SpaceId), ct);

        if (result.IsFailure)
        {
            await this.SendResultErrorsAsync(result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
