using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;
using BeanShare.Application.Features.Balances.Dtos;

namespace BeanShare.Application.Features.Balances.Queries.GetLedgerStartImpact;

/// <summary>Counts the existing, not yet billed cups a candidate ledger start date would leave out of money.</summary>
[RequireSpaceAdmin("SpaceId")]
public sealed record GetLedgerStartImpactQuery(Guid SpaceId, DateTime LedgerStartDate) : IAuthorize, IQuery<Result<LedgerStartImpactDto>>;
