using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Spaces.Dtos;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using MapsterMapper;
using MediatR;

namespace BeanShare.Application.Features.Spaces.Queries;
public sealed class GetSpaceByIdQueryHandler : IRequestHandler<GetSpaceByIdQuery, Result<SpaceDto>>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;
    private readonly IUserService _userService;
    private readonly IMapper _mapper;
    private readonly IUserRepository _userRepository;

    public GetSpaceByIdQueryHandler(
        ISpaceRepository spaceRepository,
        IUserContext userContext,
        IUserService userService,
        IMapper mapper,
        IUserRepository userRepository)
    {
        _userRepository = userRepository;
        _spaceRepository = spaceRepository;
        _userContext = userContext;
        _userService = userService;
        _mapper = mapper;
    }

    public async Task<Result<SpaceDto>> Handle(GetSpaceByIdQuery request, CancellationToken cancellationToken)
    {
        var specification = new SpaceByIdSpecification(request.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(specification, cancellationToken);

        if (space == null)
        {
            return Result<SpaceDto>.Failure(Error.SpaceNotFound(request.SpaceId.Value));
        }

        if (!space.HasMember(_userContext.CurrentUserId))
        {
            return Result<SpaceDto>.Failure(Error.InsufficientSpacePrivileges("view space details"));
        }

        var spaceDto = _mapper.Map<SpaceDto>(space);

        var userIds = space.Members.Select(m => m.UserId).ToList();
        var users = await _userService.GetByIdsAsync(userIds, cancellationToken);
        var userLookup = users.ToDictionary(u => u.Id.Value, u => u);

        var enrichedMembers = spaceDto.Members.Select(m =>
        {
            if (userLookup.TryGetValue(m.UserId, out var user))
            {
                return m with { Email = user.PublicEmail, UserName = user.Name, IsPlaceholder = user.IsPlaceholder };
            }
            // Fallback: use the current user's email if this is the current user
            if (m.UserId == _userContext.CurrentUserId.Value)
            {
                var email = _userContext.Email;
                var userName = email.Contains('@') ? email.Split('@')[0] : email;
                return m with { Email = email, UserName = userName };
            }
            return m;
        }).ToList();

        // Merged placeholders stay members for later sweeps but are hidden everywhere (DS-32); read uncached.
        var merged = await _userRepository.GetMergedMapAsync(userIds, cancellationToken);
        var visibleMembers = enrichedMembers.Where(m => !merged.ContainsKey(new UserId(m.UserId))).ToList();

        return Result<SpaceDto>.Success(spaceDto with { Members = visibleMembers, MemberCount = visibleMembers.Count });
    }
}