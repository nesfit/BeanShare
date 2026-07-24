using BeanShare.Application.Abstractions;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace BeanShare.Infrastructure.Persistence.Repositories;

public sealed class CoffeeStockRepository : ICoffeeStockRepository
{
    private readonly BeanShareDbContext _context;

    public CoffeeStockRepository(BeanShareDbContext context)
    {
        _context = context;
    }

    public async Task<CoffeeStock?> GetBySpaceIdAsync(SpaceId spaceId, CancellationToken cancellationToken = default)
    {
        var specification = new CoffeeStockBySpaceSpecification(spaceId);
        return await GetSingleBySpecAsync(specification, cancellationToken);
    }

    public async Task<CoffeeStock?> GetByIdAsync(CoffeeStockId id, CancellationToken cancellationToken = default)
    {
        var specification = new CoffeeStockByIdSpecification(id);
        return await GetSingleBySpecAsync(specification, cancellationToken);
    }

    public async Task<CoffeeStock?> GetSingleBySpecAsync<TSpec>(TSpec specification, CancellationToken cancellationToken = default)
        where TSpec : ISpec<CoffeeStock>
    {
        var query = _context.CoffeeStocks
            .Include(cs => cs.Purchases)
            .AsQueryable();

        return await query.Where(specification.Criteria).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<CoffeeStock>> GetBySpecAsync<TSpec>(TSpec specification, CancellationToken cancellationToken = default)
        where TSpec : ISpec<CoffeeStock>
    {
        var query = _context.CoffeeStocks
            .Include(cs => cs.Purchases)
            .AsQueryable();

        return await query.Where(specification.Criteria).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(CoffeeStock coffeeStock, CancellationToken cancellationToken = default)
    {
        await _context.CoffeeStocks.AddAsync(coffeeStock, cancellationToken);
    }

    public Task UpdateAsync(CoffeeStock coffeeStock, CancellationToken _ = default)
    {
        // The aggregate is always loaded within the same DbContext scope before UpdateAsync is
        // called, so it is already tracked. EF Core's snapshot change tracking detects all
        // modifications automatically at SaveChanges time:
        //   - changed scalar properties  → UPDATE
        //   - new children added to the collections → INSERT
        //   - children removed from the collections → DELETE
        //     (the FK is required/IsRequired(), so orphaned dependents are automatically deleted)
        //
        // Calling context.Update() on an already-tracked entity would reset the collection
        // snapshot to the post-mutation state, which prevents EF Core from detecting orphaned
        // children and issuing the necessary DELETE statements.
        return Task.CompletedTask;
    }

    public async Task<bool> ExistsForSpaceAsync(SpaceId spaceId, CancellationToken cancellationToken = default)
    {
        var specification = new CoffeeStockBySpaceSpecification(spaceId);
        return await _context.CoffeeStocks
            .AnyAsync(specification.Criteria, cancellationToken);
    }
}