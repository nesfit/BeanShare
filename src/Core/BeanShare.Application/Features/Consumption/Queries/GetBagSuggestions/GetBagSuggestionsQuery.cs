using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;
using BeanShare.Application.Features.Consumption.Dtos;
using BeanShare.Domain.Services;

namespace BeanShare.Application.Features.Consumption.Queries.GetBagSuggestions;

/// <summary>
/// Ranks the space's non-empty bags for the log-coffee picker: open bags first, then by how much
/// the space has been pouring from them lately (own cups weighted heavier).
/// </summary>
[RequireSpaceMember("SpaceId")]
public sealed record GetBagSuggestionsQuery(
    Guid SpaceId,
    int WindowSize = BagSuggestionPolicy.DefaultWindowSize
) : IAuthorize, IQuery<Result<BagSuggestionsResult>>;

public sealed record BagSuggestionsResult(
    IReadOnlyList<BagSuggestionDto> Bags,
    int WindowSize,
    int WindowCups,
    Guid? SuggestedLotId
);
