using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Spaces.Dtos;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using MapsterMapper;
using MediatR;

namespace BeanShare.Application.Features.Spaces.Queries;
public sealed class GetUserSpacesQueryHandler : IRequestHandler<GetUserSpacesQuery, Result<GetUserSpacesResult>>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;
    private readonly IMapper _mapper;
    private readonly IUserService _userService;

    public GetUserSpacesQueryHandler(
        ISpaceRepository spaceRepository,
        IUserContext userContext,
        IMapper mapper,
        IUserService userService)
    {
        _spaceRepository = spaceRepository;
        _userContext = userContext;
        _mapper = mapper;
        _userService = userService;
    }

    public async Task<Result<GetUserSpacesResult>> Handle(GetUserSpacesQuery request, CancellationToken cancellationToken)
    {
        var specification = new SpacesWithUserMembershipSpecification(_userContext.CurrentUserId);
        var userSpaces = await _spaceRepository.GetBySpecAsync(specification, cancellationToken);

        var spaceDtos = _mapper.Map<IReadOnlyList<SpaceSummaryDto>>(userSpaces);

        // Placeholders (merged ones included) are former or offline members, not people who use the app, so the member
        // count leaves them out, as the space's analytics do.
        var placeholders = (await _userService.GetByIdsAsync(
                userSpaces.SelectMany(s => s.Members.Select(m => m.UserId)).Distinct(), cancellationToken))
            .Where(u => u.IsPlaceholder)
            .Select(u => u.Id)
            .ToHashSet();

        // Post-process to set the current user's actual role in each space
        var userId = _userContext.CurrentUserId;
        var spacesWithRoles = new List<SpaceSummaryDto>();
        for (int i = 0; i < spaceDtos.Count; i++)
        {
            var dto = spaceDtos[i];
            var space = userSpaces[i];
            var membership = space.Members.FirstOrDefault(m => m.UserId == userId);
            if (membership != null)
            {
                dto = dto with { Role = membership.Role.ToString() };
            }
            dto = dto with { MemberCount = space.Members.Count(m => !placeholders.Contains(m.UserId)) };
            spacesWithRoles.Add(dto);
        }

        return Result<GetUserSpacesResult>.Success(new GetUserSpacesResult(spacesWithRoles));
    }
}