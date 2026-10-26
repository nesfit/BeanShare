using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;

namespace BeanShare.Application.Features.History.Commands.CommitHistoryImport;

/// <summary>
/// Saves an Excel history paste as historical Settled periods with cup counts and purchases (DS-16), all or
/// nothing; unknown members can become placeholders in the same save.
/// </summary>
[RequireSpaceAdmin("SpaceId")]
public sealed record CommitHistoryImportCommand(
    Guid SpaceId,
    string CupsText,
    string? PurchasesText = null,
    string? Note = null,
    bool CreateMissingPlaceholders = false
) : IAuthorize, ICommand<Result<HistoryImportResultDto>>;

public sealed record HistoryImportResultDto(Guid ImportId, int PeriodCount, int TotalCups, decimal TotalCost);
