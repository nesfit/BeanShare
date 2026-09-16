using System.Linq.Expressions;
using BeanShare.Domain.Entities;

namespace BeanShare.Domain.Specifications;

/// <summary>
/// The phantom-fill cups of one emptying of a bag: write-off entries drawn from the bag and created
/// at or after the moment it was emptied. Earlier write-offs of a bag that was later revived and
/// emptied again are excluded by the <paramref name="since"/> bound.
/// </summary>
public sealed class WriteOffEntriesByLotSpecification : ISpec<ConsumptionEntry>
{
    private readonly Guid _lotId;
    private readonly DateTime _since;

    public WriteOffEntriesByLotSpecification(Guid lotId, DateTime since)
    {
        if (lotId == Guid.Empty)
            throw new ArgumentException("LotId cannot be empty", nameof(lotId));

        _lotId = lotId;
        _since = since;
    }

    public Expression<Func<ConsumptionEntry, bool>> Criteria =>
        c => c.StockLotId == _lotId && c.IsWriteOff && c.CreatedAt >= _since;

    public string? Reason => $"Write-off entries for bag {_lotId} since {_since:O}";
}
