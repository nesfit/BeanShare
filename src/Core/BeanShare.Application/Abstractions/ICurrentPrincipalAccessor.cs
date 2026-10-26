using System.Security.Claims;

namespace BeanShare.Application.Abstractions;

/// <summary>
/// The raw principal of the current request, for the few reads that must work before a user row exists
/// (the pending placeholder link, DS-31). Separate from <see cref="IUserContext"/>, which MAUI implements too.
/// </summary>
public interface ICurrentPrincipalAccessor
{
    ClaimsPrincipal? Principal { get; }
}
