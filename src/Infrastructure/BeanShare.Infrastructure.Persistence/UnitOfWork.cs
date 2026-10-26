using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BeanShare.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    // Unique indexes that guard the money ledger against racing generations (design §6).
    private static readonly HashSet<string> LedgerUniqueIndexes = new(StringComparer.Ordinal)
    {
        "IX_Settlements_BillingPeriodId",
        "IX_Settlements_SpaceId_Sequence",
        "IX_SettlementLines_CarriedFromSettlementId_UserId"
    };

    private const string UsersEmailIndex = "IX_Users_Email";
    private const string UniqueViolation = "23505";

    private readonly BeanShareDbContext _context;
    private IDbContextTransaction? _currentTransaction;

    public UnitOfWork(BeanShareDbContext context)
    {
        _context = context;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _context.ChangeTracker.Clear();
            throw new ConcurrencyConflictException(RootTypes(ex.Entries), ex);
        }
        catch (DbUpdateException ex) when (UniqueConstraintName(ex) is { } index
            && (LedgerUniqueIndexes.Contains(index) || index == UsersEmailIndex))
        {
            var types = RootTypes(ex.Entries);
            _context.ChangeTracker.Clear();
            throw new ConcurrencyConflictException(types, ex,
                index == UsersEmailIndex ? Error.Codes.EmailInUse : Error.Codes.ConcurrencyConflict);
        }
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction != null)
        {
            return;
        }

        _currentTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction == null)
        {
            throw new InvalidOperationException("No active transaction to commit");
        }

        try
        {
            await SaveChangesAsync(cancellationToken);
            await _currentTransaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (_currentTransaction != null)
            {
                _currentTransaction.Dispose();
                _currentTransaction = null;
            }
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction == null)
        {
            return;
        }

        try
        {
            await _currentTransaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            if (_currentTransaction != null)
            {
                _currentTransaction.Dispose();
                _currentTransaction = null;
            }

            // Tracked entities may hold values the database never kept.
            _context.ChangeTracker.Clear();
        }
    }

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();
        return Task.CompletedTask;
    }

    private static string? UniqueConstraintName(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolation } pg ? pg.ConstraintName : null;

    /// <summary>Maps each conflicting entry to its aggregate (owner) type, so owned value objects count as their root.</summary>
    private static IReadOnlyCollection<Type> RootTypes(IEnumerable<EntityEntry> entries) =>
        entries
            .Select(e =>
            {
                var entityType = e.Metadata;
                while (entityType.FindOwnership() is { } ownership)
                    entityType = ownership.PrincipalEntityType;
                return entityType.ClrType;
            })
            .Distinct()
            .ToList();
}
