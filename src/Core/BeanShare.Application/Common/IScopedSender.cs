using MediatR;

namespace BeanShare.Application.Common;

/// <summary>
/// Sends each request in its own DI scope (fresh DbContext), for long-lived callers such as Blazor
/// circuits. Deliberately not an <see cref="ISender"/>: MediatR's interfaces are never re-registered.
/// </summary>
public interface IScopedSender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);

    Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest;
}
