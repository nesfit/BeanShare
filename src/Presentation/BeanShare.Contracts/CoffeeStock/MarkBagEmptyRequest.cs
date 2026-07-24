namespace BeanShare.Contracts.CoffeeStock;

/// <summary>
/// Body for POST /api/spaces/{spaceId}/stock/bags/{lotId}/empty. SpaceId and LotId come from the route.
/// </summary>
public sealed record MarkBagEmptyRequest
{
    public string Reason { get; init; } = "Bag emptied";
}
