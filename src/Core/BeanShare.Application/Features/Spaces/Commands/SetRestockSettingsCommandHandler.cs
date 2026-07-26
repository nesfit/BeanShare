using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Exceptions;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Spaces.Commands;

public sealed class SetRestockSettingsCommandHandler : IRequestHandler<SetRestockSettingsCommand, Result>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;

    public SetRestockSettingsCommandHandler(ISpaceRepository spaceRepository, IUserContext userContext)
    {
        _spaceRepository = spaceRepository;
        _userContext = userContext;
    }

    public async Task<Result> Handle(SetRestockSettingsCommand command, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(command.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(spaceId), cancellationToken);

        if (space is null)
        {
            return Result.Failure(Error.SpaceNotFound(command.SpaceId));
        }

        if (!space.IsAdmin(_userContext.CurrentUserId))
        {
            return Result.Failure(Error.InsufficientSpacePrivileges("change restock settings"));
        }

        try
        {
            space.SetRestockSettings(command.LeadTimeDays, command.MinOrderValue);
            await _spaceRepository.UpdateAsync(space, cancellationToken);
            return Result.Success();
        }
        catch (SpaceDomainException ex)
        {
            return Result.Failure(Error.ValidationFailure("Space.RestockSettings", ex.Message));
        }
    }
}
