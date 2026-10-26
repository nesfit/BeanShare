using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;
using BeanShare.Application.Features.History.Dtos;

namespace BeanShare.Application.Features.History.Queries.PreviewHistoryImport;

/// <summary>Parses an Excel history paste (cups block and purchases block) and reports what it would import.</summary>
[RequireSpaceAdmin("SpaceId")]
public sealed record PreviewHistoryImportQuery(
    Guid SpaceId,
    string CupsText,
    string? PurchasesText = null,
    bool CreateMissingPlaceholders = false
) : IAuthorize, IQuery<Result<HistoryImportPreviewDto>>;
