using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;
using BeanShare.Application.Features.History.Dtos;

namespace BeanShare.Application.Features.History.Queries.GetSpaceHistory;

/// <summary>The space's imported history and all-time statistics (DS-16).</summary>
[RequireSpaceMember("SpaceId")]
public sealed record GetSpaceHistoryQuery(Guid SpaceId) : IAuthorize, IQuery<Result<SpaceHistoryDto>>;
