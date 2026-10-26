using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;

namespace BeanShare.Application.Features.History.Commands.DeleteHistoryImport;

/// <summary>Deletes one history import with its periods, cup counts and purchases; money is untouched.</summary>
[RequireSpaceAdmin("SpaceId")]
public sealed record DeleteHistoryImportCommand(Guid SpaceId, Guid ImportId) : IAuthorize, ICommand<Result>;
