using BeanShare.Application.Abstractions;
using BeanShare.Application.Services;
using BeanShare.Contracts.Me;
using FastEndpoints;

namespace BeanShare.Api.Endpoints.Me;

public sealed class GetCurrentUserEndpoint : EndpointWithoutRequest<CurrentUserResponse>
{
    private readonly IUserContext _userContext;
    private readonly IUserService _userService;

    public GetCurrentUserEndpoint(IUserContext userContext, IUserService userService)
    {
        _userContext = userContext;
        _userService = userService;
    }

    public override void Configure()
    {
        Get("/api/me");
        Summary(s =>
        {
            s.Summary = "Get current authenticated user";
            s.Description = "Returns the currently authenticated user's information from claims. Requires authentication.";
            s.Response<CurrentUserResponse>(200, "Current user information");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // RevolutTag lives on the User entity, not the auth claims, so load it for the profile view.
        var user = await _userService.GetByIdAsync(_userContext.CurrentUserId, ct);

        var response = new CurrentUserResponse
        {
            UserId = _userContext.CurrentUserId.Value,
            Email = _userContext.Email,
            Roles = _userContext.Roles.ToList(),
            RevolutTag = user?.RevolutTag
        };

        await SendOkAsync(response, ct);
    }
}
