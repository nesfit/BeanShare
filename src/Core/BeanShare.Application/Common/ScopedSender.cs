using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace BeanShare.Application.Common;

public sealed class ScopedSender(IServiceScopeFactory scopeFactory) : IScopedSender
{
    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await new Mediator(scope.ServiceProvider).Send(request, ct);
    }

    public async Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await new Mediator(scope.ServiceProvider).Send(request, ct);
    }
}
