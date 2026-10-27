using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;
using BeanShare.Domain.Common;
using MediatR;

namespace BeanShare.Application.Behaviors;

/// <summary>
/// No refused, pending or unknown identity can act. For an authenticated caller, every command and
/// every <see cref="IAuthorize"/> request needs an existing, active, non-placeholder user row, read
/// uncached. Unauthenticated requests pass through unchanged; later behaviours decide about them.
/// </summary>
public sealed class CurrentUserBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, IRequest<TResponse>
{
    private readonly IUserContext _userContext;
    private readonly IUserRepository _userRepository;

    public CurrentUserBehavior(IUserContext userContext, IUserRepository userRepository)
    {
        _userContext = userContext;
        _userRepository = userRepository;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not (ICommand<TResponse> or IAuthorize))
            return await next();

        UserId userId;
        try
        {
            userId = _userContext.CurrentUserId;
        }
        catch (InvalidOperationException)
        {
            return await next();
        }

        var status = await _userRepository.GetAccessStatusAsync(userId, cancellationToken);
        if (status.CanAct)
            return await next();

        if (ResultFactory.CanCreate<TResponse>())
            return ResultFactory.Failure<TResponse>(Error.Unauthenticated());

        throw new UnauthorizedAccessException("The current user cannot act");
    }
}
