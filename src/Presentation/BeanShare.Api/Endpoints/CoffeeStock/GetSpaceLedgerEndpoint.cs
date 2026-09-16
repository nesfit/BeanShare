using BeanShare.Application.Features.CoffeeStock.Queries.GetSpaceLedger;
using BeanShare.Contracts.CoffeeStock;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.CoffeeStock;

public sealed class GetSpaceLedgerEndpoint : Endpoint<GetSpaceLedgerRequest, LedgerPageResponse>
{
    private readonly IMediator _mediator;

    public GetSpaceLedgerEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Get("/api/spaces/{spaceId}/stock/ledger");
        Summary(s =>
        {
            s.Summary = "Get the stock ledger for a space";
            s.Description = "A unified, filterable/sortable/paged ledger of stock movements: every purchase (+) and " +
                            "cup (−), each tied to its bag, with a per-product running balance and totals. Requires space membership.";
        });
    }

    public override async Task HandleAsync(GetSpaceLedgerRequest req, CancellationToken ct)
    {
        var routeSpaceId = Route<Guid>("spaceId");

        var query = new GetSpaceLedgerQuery(
            routeSpaceId,
            req.Kind,
            req.Product,
            req.MemberUserId,
            req.StartDate,
            req.EndDate,
            req.SortBy,
            req.SortDescending,
            req.Page,
            req.PageSize);

        var result = await _mediator.Send(query, ct);

        if (result.IsSuccess)
        {
            var page = result.Value;
            var response = new LedgerPageResponse
            {
                TotalCount = page.TotalCount,
                Page = page.Page,
                PageSize = page.PageSize,
                Entries = page.Entries.Select(e => new LedgerEntryResponse
                {
                    Timestamp = e.Timestamp,
                    Kind = e.Kind,
                    ProductDisplayName = e.ProductDisplayName,
                    ProductType = e.ProductType,
                    BagLabel = e.BagLabel,
                    DeltaGrams = e.DeltaGrams,
                    RunningBalanceGrams = e.RunningBalanceGrams,
                    UserId = e.UserId,
                    UserName = e.UserName,
                    CostAmount = e.CostAmount,
                    CostCurrency = e.CostCurrency,
                    Note = e.Note
                }).ToList(),
                Totals = new LedgerTotalsResponse
                {
                    PurchasedGrams = page.Totals.PurchasedGrams,
                    PurchasedCost = page.Totals.PurchasedCost,
                    ConsumedGrams = page.Totals.ConsumedGrams,
                    ConsumedCost = page.Totals.ConsumedCost,
                    NetGrams = page.Totals.NetGrams,
                    PurchaseCount = page.Totals.PurchaseCount,
                    CupCount = page.Totals.CupCount,
                    Currency = page.Totals.Currency,
                    DiscardedGrams = page.Totals.DiscardedGrams,
                    DiscardedCost = page.Totals.DiscardedCost,
                    DiscardCount = page.Totals.DiscardCount
                }
            };
            await SendOkAsync(response, ct);
        }
        else
        {
            foreach (var error in result.Errors)
            {
                AddError(error.Code, error.Message);
            }
            await SendErrorsAsync(cancellation: ct);
        }
    }
}
