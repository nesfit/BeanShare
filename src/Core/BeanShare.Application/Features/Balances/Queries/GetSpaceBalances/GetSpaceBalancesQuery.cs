using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;
using BeanShare.Application.Features.Balances.Dtos;

namespace BeanShare.Application.Features.Balances.Queries.GetSpaceBalances;

/// <summary>The money ledger of a space: settings and entries (admins see all, members their own).</summary>
[RequireSpaceMember("SpaceId")]
public sealed record GetSpaceBalancesQuery(Guid SpaceId) : IAuthorize, IQuery<Result<SpaceBalancesDto>>;
