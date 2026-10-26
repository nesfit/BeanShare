using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Balances.Dtos;
using BeanShare.Application.Features.Balances.Queries.GetLedgerStartImpact;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Balances;

public sealed class GetLedgerStartImpactRequest
{
    public Guid SpaceId { get; set; }

    public DateTime LedgerStartDate { get; set; }
}

public sealed class GetLedgerStartImpactEndpoint : Endpoint<GetLedgerStartImpactRequest, LedgerStartImpactDto>
{
    private readonly IMediator _mediator;

    public GetLedgerStartImpactEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Get("/api/spaces/{SpaceId}/ledger-settings/impact");
        Summary(s =>
        {
            s.Summary = "Count the unbilled cups a ledger start date would leave out of money";
            s.Description = "Pass the candidate date as ?ledgerStartDate=yyyy-MM-dd. Admin only.";
        });
    }

    public override async Task HandleAsync(GetLedgerStartImpactRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetLedgerStartImpactQuery(req.SpaceId, req.LedgerStartDate), ct);

        if (result.IsFailure)
        {
            await this.SendResultErrorsAsync(result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
