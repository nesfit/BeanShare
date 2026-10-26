using BeanShare.Application;
using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Common;
using BeanShare.Infrastructure;
using BeanShare.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using UserEntity = BeanShare.Domain.Entities.User;

namespace BeanShare.Tests.Integration.Concurrency;

/// <summary>
/// The web head's container (AddApplication + AddScopedSender + AddInfrastructure) on the fixture's
/// migrated PostgreSQL database, with a test user context and a <see cref="BarrierInterceptor"/>.
/// Npgsql variants of the race tests assert end states, which EF InMemory cannot (not atomic).
/// </summary>
public sealed class NpgsqlAppContainer : IAsyncDisposable
{
    private NpgsqlAppContainer(ServiceProvider services, TestUserContext user, BarrierInterceptor barrier)
    {
        Services = services;
        User = user;
        Barrier = barrier;
    }

    public ServiceProvider Services { get; }
    public TestUserContext User { get; }
    public BarrierInterceptor Barrier { get; }

    public static NpgsqlAppContainer Create(string connectionString)
    {
        var user = new TestUserContext();
        var barrier = new BarrierInterceptor();

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddScopedSender();
        services.AddInfrastructure(connectionString);
        services.AddHttpContextAccessor();
        services.AddLogging();
        services.AddSingleton<IUserContext>(user);
        services.ConfigureDbContext<BeanShareDbContext>((_, options) => options.AddInterceptors(barrier));

        return new NpgsqlAppContainer(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }), user, barrier);
    }

    public async Task<UserId> SeedUserAsync(string name)
    {
        var id = Guid.NewGuid();
        var user = UserEntity.CreateWithIdAndPassword(id, $"{id:N}@example.org", name, "hash", DateTime.UtcNow);
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BeanShareDbContext>();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    public async Task<TResponse> SendAsync<TResponse>(UserId actor, IRequest<TResponse> request)
    {
        User.Set(actor);
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IScopedSender>().Send(request);
    }

    public async Task<T> ReadAsync<T>(Func<BeanShareDbContext, Task<T>> read)
    {
        await using var scope = Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<BeanShareDbContext>());
    }

    public ValueTask DisposeAsync() => Services.DisposeAsync();
}

public sealed class TestUserContext : IUserContext
{
    private readonly AsyncLocal<UserId?> _current = new();

    public UserId CurrentUserId => _current.Value ?? throw new InvalidOperationException("No acting user");
    public string Email => string.Empty;
    public IReadOnlyCollection<string> Roles => Array.Empty<string>();

    public void Set(UserId userId) => _current.Value = userId;
}

/// <summary>
/// One-shot barrier: after <see cref="Arm"/>, the next SaveChanges (after that command's reads) waits
/// until <see cref="Release"/>. Retried or commit-path saves are never held.
/// </summary>
public sealed class BarrierInterceptor : SaveChangesInterceptor
{
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _gate = new(0, 1);
    private TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _armed;

    public void Arm()
    {
        _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _armed, 1);
    }

    public Task WaitUntilHeldAsync() => _held.Task.WaitAsync(HangGuard);

    public void Release() => _gate.Release();

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _armed, 0) == 1)
        {
            _held.TrySetResult();
            if (!await _gate.WaitAsync(HangGuard, cancellationToken))
                throw new TimeoutException("The held save was never released");
        }

        return result;
    }
}
