using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Aggregates.Space;
using MediatR;

namespace BeanShare.Application.Behaviors;

/// <summary>
/// Re-runs an <see cref="IRetryOnConcurrencyConflict"/> command (at most <see cref="MaxRetries"/> times,
/// each in a cleared change tracker) when it lost a race on bag counters only, or, for a new cup
/// (<see cref="IRetryOnLedgerTokenConflict"/>), on bag counters and the space's ledger token. Any other conflict (on
/// the cup itself, for example), or one that persists, becomes a CONCURRENCY_CONFLICT failure.
/// </summary>
public sealed class ConcurrencyRetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public const int MaxRetries = 2;

    private readonly IUnitOfWork _unitOfWork;

    public ConcurrencyRetryBehavior(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IRetryOnConcurrencyConflict)
        {
            return await next();
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await next();
            }
            catch (ConcurrencyConflictException ex)
            {
                var ledgerToken = request is IRetryOnLedgerTokenConflict;
                var onlyRetryable = ex.EntityTypes.Count > 0
                    && ex.EntityTypes.All(t => t == typeof(Purchase) || (ledgerToken && t == typeof(Space)));
                if (!onlyRetryable || attempt >= MaxRetries)
                {
                    await _unitOfWork.ResetAsync(cancellationToken);
                    return ResultFactory.Failure<TResponse>(ex.ToError());
                }

                await _unitOfWork.ResetAsync(cancellationToken);
            }
        }
    }
}
