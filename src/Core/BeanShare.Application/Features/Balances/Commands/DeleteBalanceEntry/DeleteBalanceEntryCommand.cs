using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;

namespace BeanShare.Application.Features.Balances.Commands.DeleteBalanceEntry;

/// <summary>Deletes an entry no settlement has included yet (otherwise ENTRY_LOCKED).</summary>
[RequireSpaceAdmin("SpaceId")]
public sealed record DeleteBalanceEntryCommand(Guid SpaceId, Guid EntryId) : IAuthorize, ICommand<Result<bool>>;
