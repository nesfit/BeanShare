using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Settlement.Dtos;

namespace BeanShare.Application.Features.Settlement.Queries.GetSettlementPreview;

/// <summary>
/// Runs the settlement calculation of a closed period with exactly the generation parameters, so the
/// preview equals what generation will store. Admin only.
/// </summary>
public sealed record GetSettlementPreviewQuery(Guid BillingPeriodId) : IQuery<Result<SettlementPreviewDto>>;
