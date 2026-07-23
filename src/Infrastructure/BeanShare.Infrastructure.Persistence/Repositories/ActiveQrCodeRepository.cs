using BeanShare.Application.Abstractions;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace BeanShare.Infrastructure.Persistence.Repositories;

public sealed class ActiveQrCodeRepository : IActiveQrCodeRepository
{
    private readonly BeanShareDbContext _context;

    public ActiveQrCodeRepository(BeanShareDbContext context)
    {
        _context = context;
    }

    public async Task<ActiveQrCode?> GetByIdAsync(ActiveQrCodeId id, CancellationToken cancellationToken = default)
    {
        return await _context.ActiveQrCodes
            .FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
    }

    public async Task<ActiveQrCode?> GetSingleBySpecAsync(ISpec<ActiveQrCode> spec, CancellationToken cancellationToken = default)
    {
        return await _context.ActiveQrCodes
            .Where(spec.Criteria)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActiveQrCode>> GetBySpaceIdAsync(SpaceId spaceId, CancellationToken cancellationToken = default)
    {
        return await _context.ActiveQrCodes
            .Where(q => q.SpaceId == spaceId && q.IsActive)
            .OrderByDescending(q => q.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(ActiveQrCode entity, CancellationToken cancellationToken = default)
    {
        await _context.ActiveQrCodes.AddAsync(entity, cancellationToken);
    }

    public Task UpdateAsync(ActiveQrCode entity, CancellationToken cancellationToken = default)
    {
        // ActiveQrCode is always loaded within the same DbContext scope before UpdateAsync is
        // called, so it is already tracked. EF Core's snapshot change tracking persists only
        // the modified scalar properties automatically.
        return Task.CompletedTask;
    }
}
