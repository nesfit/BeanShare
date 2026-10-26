using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;
using BeanShare.Application.Features.Balances.Dtos;
using BeanShare.Domain.Enums;

namespace BeanShare.Application.Features.Balances.Queries.PreviewBalanceEntriesPaste;

/// <summary>
/// Parses a pasted <c>member;amount;note</c> batch and reports matches, errors and the sum, saving nothing.
/// </summary>
[RequireSpaceAdmin("SpaceId")]
public sealed record PreviewBalanceEntriesPasteQuery(
    Guid SpaceId,
    string Text,
    string? BatchNote = null,
    Guid? CounterpartyUserId = null,
    DateTime? EffectiveDate = null,
    BalanceEntryKind Kind = BalanceEntryKind.Opening,
    bool CreateMissingPlaceholders = false
) : IAuthorize, IQuery<Result<BalancePastePreviewDto>>;
