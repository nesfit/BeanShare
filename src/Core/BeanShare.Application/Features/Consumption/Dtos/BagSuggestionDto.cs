namespace BeanShare.Application.Features.Consumption.Dtos;

/// <summary>One bag offered in the log-coffee picker, with why it ranks where it does.</summary>
public sealed class BagSuggestionDto
{
    public required Guid LotId { get; init; }

    /// <summary>Per-product bag label ("#2").</summary>
    public required string Label { get; init; }

    /// <summary>Id of the product rollup this bag belongs to (the old product-level picker key).</summary>
    public required Guid StockLevelId { get; init; }

    public required string ProductName { get; init; }
    public required string ProductBrand { get; init; }
    public required string ProductType { get; init; }
    public required string ProductDisplayName { get; init; }

    public required decimal RemainingGrams { get; init; }
    public required string Status { get; init; }
    public required bool IsOpen { get; init; }
    public DateTime? OpenedAt { get; init; }

    /// <summary>Cups drawn from this bag inside the recent-cup window.</summary>
    public required int RecentCups { get; init; }

    /// <summary>How many of <see cref="RecentCups"/> were the current user's.</summary>
    public required int MyRecentCups { get; init; }

    public DateTime? LastUsedAt { get; init; }

    /// <summary>True for the single bag the picker should preselect.</summary>
    public required bool IsSuggested { get; init; }

    /// <summary>Human-readable justification, set only on the suggested bag.</summary>
    public string? SuggestionReason { get; init; }
}
