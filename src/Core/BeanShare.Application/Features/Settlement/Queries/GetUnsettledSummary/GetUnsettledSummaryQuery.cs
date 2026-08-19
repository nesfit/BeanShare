using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Settlement.Dtos;

namespace BeanShare.Application.Features.Settlement.Queries.GetUnsettledSummary;

/// <summary>
/// How many cups — and how much money — the caller has run up since the last settlement.
/// </summary>
/// <param name="SpaceId">
/// A single space, or <c>null</c> for every space the caller is a member of (the dashboard case).
/// </param>
public sealed record GetUnsettledSummaryQuery(Guid? SpaceId = null) : IQuery<Result<UnsettledSummaryResult>>;
