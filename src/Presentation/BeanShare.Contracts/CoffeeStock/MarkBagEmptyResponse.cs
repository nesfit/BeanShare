namespace BeanShare.Contracts.CoffeeStock;

/// <summary>Result of emptying a bag: the remainder that was phantom-filled to its drinkers.</summary>
public sealed record MarkBagEmptyResponse
{
    public required Guid LotId { get; init; }
    public required string ProductDisplayName { get; init; }
    public required string BagLabel { get; init; }
    public required decimal DistributedGrams { get; init; }
    public required int MemberCount { get; init; }
    public required string Message { get; init; }
}
