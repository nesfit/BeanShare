using BeanShare.Application.Common;
using BeanShare.Domain.Exceptions;
using MediatR;

namespace BeanShare.Application.Behaviors;

public sealed class ExceptionHandlingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        try
        {
            return await next();
        }
        catch (SpaceDomainException ex)
        {
            return ResultFactory.Failure<TResponse>(Error.DomainError(ex.Message));
        }
        catch (LedgerDomainException ex)
        {
            return ResultFactory.Failure<TResponse>(new Error(ex.Code, ex.Message));
        }
        catch (DomainException ex)
        {
            return ResultFactory.Failure<TResponse>(Error.DomainError(ex.Message));
        }
    }
}
