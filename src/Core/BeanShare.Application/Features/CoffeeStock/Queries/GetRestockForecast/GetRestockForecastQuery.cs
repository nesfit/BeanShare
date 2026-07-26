using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;

namespace BeanShare.Application.Features.CoffeeStock.Queries.GetRestockForecast;

/// <summary>
/// Forecasts when each product will run out from its recent consumption velocity, and builds a
/// lead-time-aware restock shopping list (suggested quantities + basket cost). Tuned for batch
/// ordering: it flags products that will run out within the delivery lead time (+ a safety buffer)
/// so an order placed now arrives before the stock is gone.
/// </summary>
[RequireSpaceMember("SpaceId")]
public sealed record GetRestockForecastQuery(Guid SpaceId) : IAuthorize, IQuery<Result<RestockForecastDto>>;

public sealed record RestockForecastDto(
    IReadOnlyList<RestockItemDto> Items,
    int NeedsRestockCount,
    decimal SuggestedBasketCost,
    string Currency,
    int LeadTimeDays,
    decimal MinOrderValue,
    bool WorthOrdering);

public sealed record RestockItemDto(
    string ProductDisplayName,
    string ProductType,
    decimal OnHandGrams,
    double VelocityGramsPerDay,
    int? DaysToEmpty,
    DateTime? RunOutDate,
    string Status,                 // "critical" | "soon" | "ok"
    int SuggestedReorderGrams,
    decimal? EstimatedCost);
