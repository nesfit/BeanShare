using System.Globalization;
using System.Text.RegularExpressions;
using BeanShare.Application.Features.Balances.Dtos;
using BeanShare.Domain.Aggregates.Ledger;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Application.Features.Balances.Services;

/// <summary>
/// The <c>member;amount;note</c> paste grammar (design §8.3): one record per line, <c>;</c>-separated,
/// trimmed cells, blank lines and an optional <c>member</c> header skipped, <c>.</c> or <c>,</c> decimals.
/// Shared by the preview and the all-or-nothing commit, so both judge rows identically.
/// </summary>
public static partial class BalancePasteEvaluator
{
    public const int MaxRows = 1000;

    public sealed record Batch(
        UserId CounterpartyUserId,
        DateTime EffectiveDate,
        BalanceEntryKind Kind,
        string? BatchNote,
        Currency Currency,
        DateTime? LedgerStartDate);

    public sealed record Evaluation(IReadOnlyList<BalancePasteRowDto> Rows, IReadOnlyList<string> Errors)
    {
        public bool IsValid => Errors.Count == 0 && Rows.Count > 0 && Rows.All(r => r.IsValid);
        public decimal Total => Rows.Where(r => r.IsValid).Sum(r => r.Amount ?? 0m);

        /// <summary>The distinct member texts that become placeholders on commit (DS-17), in first-seen order.</summary>
        public IReadOnlyList<string> PlaceholdersToCreate => Rows
            .Where(r => r.IsNewPlaceholder)
            .Select(r => r.MemberText.Trim())
            .DistinctBy(t => t.ToLowerInvariant())
            .ToList();
    }

    /// <summary>Batch defaults: counterparty = the caller, date = the ledger start date, else today.</summary>
    public static Batch CreateBatch(
        Domain.Aggregates.Space.Space space,
        UserId caller,
        Guid? counterpartyUserId,
        DateTime? effectiveDate,
        BalanceEntryKind kind,
        string? batchNote,
        IClock clock) =>
        new(
            counterpartyUserId is { } cp ? new UserId(cp) : caller,
            BalanceEntry.NormalizeDate(effectiveDate ?? space.LedgerStartDate ?? clock.UtcNow.Date),
            kind,
            batchNote,
            space.Currency,
            space.LedgerStartDate);

    /// <param name="createPlaceholders">An unknown member becomes a placeholder instead of an error (DS-17).</param>
    public static Evaluation Evaluate(string? text, Batch batch, MemberResolver members, IClock clock, bool createPlaceholders = false)
    {
        var errors = new List<string>();
        var rows = new List<BalancePasteRowDto>();

        var records = (text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select((line, index) => (Line: line, Number: index + 1))
            .Where(r => !string.IsNullOrWhiteSpace(r.Line))
            .ToList();

        if (records.Count > 0 && string.Equals(SplitCells(records[0].Line)[0], "member", StringComparison.OrdinalIgnoreCase))
            records.RemoveAt(0);

        if (records.Count == 0)
            errors.Add("Paste at least one row");
        if (records.Count > MaxRows)
            errors.Add($"At most {MaxRows} rows can be pasted at once");

        if (!members.IsMember(batch.CounterpartyUserId))
            errors.Add("The counterparty is not a member of this space");

        var seen = new HashSet<UserId>();
        foreach (var (line, number) in records.Take(MaxRows))
        {
            var cells = SplitCells(line);
            var rowErrors = new List<string>();
            var warnings = new List<string>();

            if (cells.Length < 2 || cells.Length > 3)
                rowErrors.Add("Expected member;amount;note");

            var memberText = cells[0];
            var match = members.Resolve(memberText);
            var isNewPlaceholder = createPlaceholders && match.IsUnknown;
            if (isNewPlaceholder)
            {
                warnings.Add($"A placeholder member will be created for '{memberText.Trim()}'");
                match = new MemberMatch(PlaceholderIdFor(memberText), memberText.Trim(), null);
            }
            else if (match.Error is not null)
            {
                rowErrors.Add(match.Error);
            }

            decimal? amount = null;
            if (cells.Length >= 2)
            {
                if (TryParseAmount(cells[1], out var parsed))
                    amount = parsed;
                else
                    rowErrors.Add($"'{cells[1]}' is not an amount");
            }

            var note = cells.Length >= 3 && cells[2].Length > 0 ? cells[2] : batch.BatchNote?.Trim() ?? string.Empty;

            if (match.UserId is { } userId && amount is { } value)
            {
                var entryError = BalanceEntry.GetValidationError(
                    userId, batch.CounterpartyUserId, value, batch.Currency, batch.EffectiveDate,
                    batch.Kind, note, batch.LedgerStartDate, clock);
                if (entryError is not null)
                    rowErrors.Add(entryError);

                if (!seen.Add(userId))
                    warnings.Add($"{match.Name} appears more than once");
            }

            rows.Add(new BalancePasteRowDto(
                number, memberText, isNewPlaceholder ? null : match.UserId?.Value, match.Name, amount, note, rowErrors, warnings, isNewPlaceholder));
        }

        return new Evaluation(rows, errors);
    }

    public static bool TryParseAmount(string text, out decimal amount)
    {
        amount = 0m;
        var value = text.Trim();
        if (!AmountPattern().IsMatch(value))
            return false;

        return decimal.TryParse(value.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out amount);
    }

    /// <summary>A stable stand-in id per new placeholder text, so validation and duplicate warnings work before it exists.</summary>
    private static UserId PlaceholderIdFor(string text)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(text.Trim().ToLowerInvariant()));
        return new UserId(new Guid(bytes));
    }

    private static string[] SplitCells(string line) => line.Split(';').Select(c => c.Trim()).ToArray();

    [GeneratedRegex(@"^[+-]?\d+([.,]\d+)?$")]
    private static partial Regex AmountPattern();
}
