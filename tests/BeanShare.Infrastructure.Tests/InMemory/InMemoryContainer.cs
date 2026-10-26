using BeanShare.Application;
using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Common;
using UserEntity = BeanShare.Domain.Entities.User;
using BeanShare.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace BeanShare.Infrastructure.Tests.InMemory;

/// <summary>
/// AddPersistence uses the fixed InMemory database name "BeanShareInMemory", so every class that
/// builds an <see cref="InMemoryContainer"/> runs in this collection, one test at a time.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class InMemoryContainerCollection
{
    public const string Name = "InMemoryContainer";
}

/// <summary>
/// The service container BlazorWeb builds (Program.cs), on the EF InMemory provider, with a test user
/// context and a <see cref="BarrierInterceptor"/> for deterministic races.
/// </summary>
public sealed class InMemoryContainer : IAsyncDisposable
{
    private InMemoryContainer(ServiceProvider services, TestUserContext user, BarrierInterceptor barrier)
    {
        Services = services;
        User = user;
        Barrier = barrier;
    }

    public ServiceProvider Services { get; }
    public TestUserContext User { get; }
    public BarrierInterceptor Barrier { get; }

    public static async Task<InMemoryContainer> CreateAsync()
    {
        var user = new TestUserContext();
        var barrier = new BarrierInterceptor();

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddScopedSender();
        services.AddInfrastructure("", useInMemoryDatabase: true);
        services.AddHttpContextAccessor();
        services.AddLogging();
        services.AddSingleton<IUserContext>(user);
        services.ConfigureDbContext<BeanShareDbContext>((_, options) => options.AddInterceptors(barrier));

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BeanShareDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }

        return new InMemoryContainer(provider, user, barrier);
    }

    public async Task<UserId> SeedUserAsync(string email, string name)
    {
        var user = UserEntity.CreateWithIdAndPassword(Guid.NewGuid(), email, name, "hash", DateTime.UtcNow);
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BeanShareDbContext>();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Sends as <paramref name="actor"/> through a fresh <see cref="IScopedSender"/>, like one web operation.</summary>
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

/// <summary>The acting user flows with the async call (one value per logical operation, no shared static state).</summary>
public sealed class TestUserContext : IUserContext
{
    private readonly AsyncLocal<UserId?> _current = new();

    public UserId CurrentUserId => _current.Value ?? throw new InvalidOperationException("No acting user");
    public string Email => string.Empty;
    public IReadOnlyCollection<string> Roles => Array.Empty<string>();

    public void Set(UserId userId) => _current.Value = userId;
}

/// <summary>
/// One-shot barrier: after <see cref="Arm"/>, the next SaveChanges (which comes after that command's
/// reads) waits until <see cref="Release"/>. Retried or commit-path saves are never held.
/// </summary>
public sealed class BarrierInterceptor : SaveChangesInterceptor
{
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _gate = new(0, 1);
    private TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _armed;

    public void Arm()
    {
        _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _armed, 1);
    }

    /// <summary>Completes once the armed save is being held; fails the test instead of hanging.</summary>
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
