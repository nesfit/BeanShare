using BeanShare.Application.Abstractions;
using BeanShare.Domain.Common;
using MediatR;

namespace BeanShare.Infrastructure.Persistence;

/// <summary>Publishes domain events through MediatR's own publisher of the current operation scope.</summary>
public sealed class DomainEventDispatcher(
    BeanShareDbContext context,
    IPublisher publisher) : IDomainEventDispatcher
{
    public IReadOnlyList<IDomainEvent> CollectDomainEvents()
        => context.CollectDomainEvents();

    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in events)
            await publisher.Publish(domainEvent, cancellationToken);
    }
}
