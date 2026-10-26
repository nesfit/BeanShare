using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;
using BeanShare.Application.Features.Balances.Dtos;
using BeanShare.Domain.Enums;

namespace BeanShare.Application.Features.Balances.Commands.CommitBalanceEntriesPaste;

/// <summary>Saves a pasted <c>member;amount;note</c> batch: every row, or none when any row is invalid.</summary>
[RequireSpaceAdmin("SpaceId")]
public sealed record CommitBalanceEntriesPasteCommand(
    Guid SpaceId,
    string Text,
    string? BatchNote = null,
    Guid? CounterpartyUserId = null,
    DateTime? EffectiveDate = null,
    BalanceEntryKind Kind = BalanceEntryKind.Opening
) : IAuthorize, ICommand<Result<IReadOnlyList<BalanceEntryDto>>>;
