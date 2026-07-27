using BeanShare.Application.Features.Consumption.Queries;
using BeanShare.Application.Features.Consumption.Queries.GetBagSuggestions;
using BeanShare.Application.Features.CoffeeStock.Queries;
using BeanShare.Application.Features.Presets.Queries;
using BeanShare.Application.Features.Spaces.Queries;
using BeanShare.Domain.ValueObjects;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Spaces;

public sealed class GetSpaceDashboardRequest
{
    public Guid SpaceId { get; set; }
}

/// <summary>
/// Composite endpoint that returns space info, stock, recent consumption, and quick presets
/// in a single round-trip. Designed for mobile clients where network latency matters.
/// </summary>
public sealed class GetSpaceDashboardEndpoint(IMediator mediator)
    : Endpoint<GetSpaceDashboardRequest>
{
    public override void Configure()
    {
        Get("/api/spaces/{SpaceId}/dashboard");
        Summary(s =>
        {
            s.Summary = "Get space dashboard data";
            s.Description = "Returns space details, stock levels, recent consumption, and quick presets in a single call. Optimized for mobile clients.";
        });
    }

    public override async Task HandleAsync(GetSpaceDashboardRequest req, CancellationToken ct)
    {
        // Sequential on purpose: every handler resolves the same scoped DbContext, which throws on
        // concurrent use. The win here is collapsing five round-trips into one, not parallelism.
        var spaceResult = await mediator.Send(new GetSpaceByIdQuery(new SpaceId(req.SpaceId)), ct);
        if (!spaceResult.IsSuccess)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var stockResult = await mediator.Send(new GetSpaceStockQuery(req.SpaceId), ct);
        var consumptionResult = await mediator.Send(new GetRecentConsumptionsQuery(req.SpaceId, null), ct);
        var presetsResult = await mediator.Send(new GetQuickPresetsQuery(req.SpaceId), ct);
        var bagsResult = await mediator.Send(new GetBagSuggestionsQuery(req.SpaceId), ct);

        var space = spaceResult.Value;
        var stock = stockResult.IsSuccess ? stockResult.Value : null;
        var consumption = consumptionResult.IsSuccess ? consumptionResult.Value : null;
        var presets = presetsResult.IsSuccess ? presetsResult.Value : null;
        var bags = bagsResult.IsSuccess ? bagsResult.Value : null;

        var response = new
        {
            Space = new
            {
                space.Id,
                space.Name,
                space.InviteCode,
                space.CurrencyCode,
                space.IsActive,
                space.MemberCount,
                space.CreatedAt,
                Members = space.Members
            },
            Stock = stock != null ? new
            {
                stock.TotalCurrentStockGrams,
                stock.TotalInvestmentAmount,
                stock.TotalInvestmentCurrency,
                // Must stay a superset of StockLevelDto's `required` members — clients bind this
                // straight into that DTO, and a missing one makes the whole dashboard payload
                // unreadable (System.Text.Json throws on absent required properties).
                StockLevels = stock.StockLevels.Select(sl => new
                {
                    sl.Id, sl.ProductName, sl.ProductBrand, sl.ProductType,
                    sl.ProductDisplayName, sl.CurrentStockGrams, sl.TotalPurchasedGrams,
                    sl.TotalConsumedGrams, sl.ConsumptionPercentage, sl.IsArchived,
                    sl.IsCurrentlyUsed, sl.UpdatedAt
                })
            } : null,
            Consumption = consumption,
            Presets = presets,
            Bags = bags
        };

        await SendOkAsync(response, ct);
    }
}
