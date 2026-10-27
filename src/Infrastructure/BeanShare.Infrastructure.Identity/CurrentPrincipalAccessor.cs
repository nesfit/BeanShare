using System.Security.Claims;
using BeanShare.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace BeanShare.Infrastructure.Identity;

/// <summary>The principal of the current HTTP request.</summary>
public sealed class CurrentPrincipalAccessor : ICurrentPrincipalAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentPrincipalAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;
}
