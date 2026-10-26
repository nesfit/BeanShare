using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using MediatR;

namespace BeanShare.Application.Behaviors;

public sealed class UnitOfWorkBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IUnitOfWork _uow;
    private readonly IDomainEventDispatcher _eventDispatcher;

    public UnitOfWorkBehavior(IUnitOfWork uow, IDomainEventDispatcher eventDispatcher)
    {
        _uow = uow;
        _eventDispatcher = eventDispatcher;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var response = await next();

        var isCommand = request.GetType().GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));

        if (isCommand && response != null)
        {
            var responseType = response.GetType();

            bool isResult = responseType == typeof(Result);
            bool isGenericResult = responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>);

            if (isResult || isGenericResult)
            {
                var isSuccessProperty = responseType.GetProperty("IsSuccess");
                if (isSuccessProperty != null)
                {
                    var isSuccess = (bool)isSuccessProperty.GetValue(response)!;

                    if (isSuccess)
                    {
                        var domainEvents = _eventDispatcher.CollectDomainEvents();

                        try
                        {
                            await _uow.SaveChangesAsync(ct);
                        }
                        catch (ConcurrencyConflictException ex) when (request is not IRetryOnConcurrencyConflict)
                        {
                            // Retryable requests let the exception reach ConcurrencyRetryBehavior,
                            // which needs the conflicting entity types.
                            return ResultFactory.Failure<TResponse>(ex.ToError());
                        }

                        if (domainEvents.Count > 0)
                            await _eventDispatcher.DispatchAsync(domainEvents, ct);
                    }
                }
            }
        }

        return response;
    }
}
