using System.Linq.Expressions;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Domain.Specifications;

public sealed class LowStockSpecification : ISpec<CoffeeStock>
{
    private readonly SpaceId _spaceId;
    private readonly Weight _threshold;

    public LowStockSpecification(SpaceId spaceId, Weight threshold)
    {
        if (spaceId.Value == Guid.Empty)
            throw new ArgumentException("SpaceId cannot be empty", nameof(spaceId));

        _spaceId = spaceId;
        _threshold = threshold;
    }

    // Stock levels are now a derived (non-persisted) projection, so low-stock can no longer be a SQL
    // predicate. This fetches the space's stock; the handler computes low-stock in memory from the
    // bags via CoffeeStock.GetLowStockProducts(threshold).
    public Expression<Func<CoffeeStock, bool>> Criteria => cs => cs.SpaceId == _spaceId;

    public string? Reason => $"Coffee stocks for space {_spaceId} with quantity below {_threshold}";
}