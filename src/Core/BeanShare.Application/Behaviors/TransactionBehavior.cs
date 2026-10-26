using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using MediatR;

namespace BeanShare.Application.Behaviors;

public sealed class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IUnitOfWork _unitOfWork;

    public TransactionBehavior(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var isCommand = request.GetType().GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));

        if (!isCommand)
        {
            return await next();
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var response = await next();

            var isSuccess = CheckIfSuccessful(response);

            if (isSuccess)
            {
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
            }
            else
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            }

            return response;
        }
        catch (ConcurrencyConflictException ex) when (request is not IRetryOnConcurrencyConflict && ResultFactory.CanCreate<TResponse>())
        {
            // A conflict on the commit-path save (e.g. notification rows): already rolled back.
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            return ResultFactory.Failure<TResponse>(ex.ToError());
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private static bool CheckIfSuccessful(TResponse? response)
    {
        if (response == null)
        {
            return false;
        }

        var responseType = response.GetType();
        var isResult = responseType == typeof(Result);
        var isGenericResult = responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>);

        if (isResult || isGenericResult)
        {
            var isSuccessProperty = responseType.GetProperty(nameof(Result.IsSuccess));
            if (isSuccessProperty != null)
            {
                return (bool)isSuccessProperty.GetValue(response)!;
            }
        }

        return false;
    }
}
