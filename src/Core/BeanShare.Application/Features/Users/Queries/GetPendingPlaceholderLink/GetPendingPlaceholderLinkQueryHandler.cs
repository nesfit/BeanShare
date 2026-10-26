using BeanShare.Application.Abstractions;
using BeanShare.Application.Features.Balances.Services;
using BeanShare.Application.Services;
using BeanShare.Domain.Specifications;
using MediatR;

namespace BeanShare.Application.Features.Users.Queries.GetPendingPlaceholderLink;

public sealed class GetPendingPlaceholderLinkQueryHandler : IRequestHandler<GetPendingPlaceholderLinkQuery, PendingPlaceholderLinkDto?>
{
    private readonly ICurrentPrincipalAccessor _principalAccessor;
    private readonly IUserRepository _userRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly LedgerSnapshotService _ledgerSnapshotService;

    public GetPendingPlaceholderLinkQueryHandler(
        ICurrentPrincipalAccessor principalAccessor,
        IUserRepository userRepository,
        ISpaceRepository spaceRepository,
        LedgerSnapshotService ledgerSnapshotService)
    {
        _principalAccessor = principalAccessor;
        _userRepository = userRepository;
        _spaceRepository = spaceRepository;
        _ledgerSnapshotService = ledgerSnapshotService;
    }

    public async Task<PendingPlaceholderLinkDto?> Handle(GetPendingPlaceholderLinkQuery request, CancellationToken cancellationToken)
    {
        // The guard (V3): only a pending principal with a verified email, keyed by that email alone.
        var principal = _principalAccessor.Principal;
        if (principal?.Identity?.IsAuthenticated != true
            || !OidcClaims.HasLinkPending(principal)
            || !OidcClaims.IsEmailVerified(principal)
            || OidcClaims.EmailOf(principal) is not { } email)
        {
            return null;
        }

        var placeholder = await _userRepository.FindByEmailAsync(email, cancellationToken);
        if (placeholder is not { IsPlaceholder: true, IsMerged: false })
            return null;

        var spaces = await _spaceRepository.GetBySpecAsync(new SpacesWithUserMembershipSpecification(placeholder.Id), cancellationToken);
        var result = new List<PendingPlaceholderSpaceDto>();
        foreach (var space in spaces)
        {
            var snapshot = await _ledgerSnapshotService.RunningAsync(space, cancellationToken);
            result.Add(new PendingPlaceholderSpaceDto(space.Id.Value, space.Name, space.Currency.Code, snapshot.Calculation.NetFor(placeholder.Id)));
        }

        return new PendingPlaceholderLinkDto(placeholder.Name, placeholder.Email, result);
    }
}
