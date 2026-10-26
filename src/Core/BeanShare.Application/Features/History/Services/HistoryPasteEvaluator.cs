using System.Globalization;
using BeanShare.Application.Features.Balances.Services;
using BeanShare.Application.Features.History.Dtos;
using BeanShare.Domain.Aggregates.History;
using BeanShare.Domain.Common;

namespace BeanShare.Application.Features.History.Services;

/// <summary>
/// The two history paste grammars (design §8.3; D23): <c>period;start;end;member;cups</c> and
/// <c>period;date;paidBy;description;grams;cost</c>. Shared by the preview and the all-or-nothing commit.
/// </summary>
public static class HistoryPasteEvaluator
{
    public const int MaxRows = 1000;
    public const int MaxLabelLength = 100;

    public sealed record Period(string Label, DateTime StartDate, DateTime EndDate);

    /// <param name="NewMemberText">Set when the member becomes a placeholder on commit.</param>
    public sealed record CupRow(int LineNumber, string Label, UserId? UserId, string? NewMemberText, string MemberName, int Cups);

    public sealed record PurchaseRow(
        int LineNumber,
        string Label,
        DateTime? PurchasedAt,
        UserId? PaidBy,
        string? NewPayerText,
        string? PayerName,
        string Description,
        decimal? Grams,
        decimal Cost);

    public sealed record Evaluation(
        IReadOnlyList<Period> Periods,
        IReadOnlyList<CupRow> CupRows,
        IReadOnlyList<PurchaseRow> PurchaseRows,
        IReadOnlyList<HistoryPreviewRowErrorDto> RowErrors,
        IReadOnlyList<string> Errors)
    {
        public bool IsValid => Errors.Count == 0 && RowErrors.Count == 0 && Periods.Count > 0;

        public IReadOnlyList<string> PlaceholdersToCreate => CupRows.Select(r => r.NewMemberText)
            .Concat(PurchaseRows.Select(r => r.NewPayerText))
            .Where(t => t is not null)
            .Select(t => t!)
            .DistinctBy(t => t.ToLowerInvariant())
            .ToList();
    }

    public static Evaluation Evaluate(
        string? cupsText,
        string? purchasesText,
        MemberResolver members,
        DateTime? ledgerStartDate,
        bool createPlaceholders)
    {
        var errors = new List<string>();
        var rowErrors = new List<HistoryPreviewRowErrorDto>();

        if (ledgerStartDate is null)
            errors.Add("Set the ledger start date first: history must end before it (DS-16)");

        var periods = new Dictionary<string, Period>(StringComparer.OrdinalIgnoreCase);
        var cupRows = new List<CupRow>();
        var seen = new HashSet<(string, string)>();

        var cupRecords = Records(cupsText, "period");
        if (cupRecords.Count == 0)
            errors.Add("Paste at least one period;start;end;member;cups row");
        if (cupRecords.Count > MaxRows)
            errors.Add($"At most {MaxRows} cup rows can be pasted at once");

        foreach (var (line, number) in cupRecords.Take(MaxRows))
        {
            var cells = Cells(line);
            var problems = new List<string>();
            if (cells.Length != 5)
            {
                rowErrors.Add(new("cups", number, line, ["Expected period;start;end;member;cups"]));
                continue;
            }

            var label = cells[0];
            if (label.Length == 0 || label.Length > MaxLabelLength)
                problems.Add($"The period label must be 1–{MaxLabelLength} characters");

            var start = ParseDate(cells[1], "start", problems);
            var end = ParseDate(cells[2], "end", problems);
            if (start is { } s && end is { } e)
            {
                if (s > e)
                    problems.Add("The start is after the end");
                else if (periods.TryGetValue(label, out var known) && (known.StartDate != s || known.EndDate != e))
                    problems.Add($"Period '{label}' already has the dates {known.StartDate:yyyy-MM-dd} to {known.EndDate:yyyy-MM-dd}");
                else if (problems.Count == 0)
                    periods.TryAdd(label, new Period(label, s, e));
            }

            var member = ResolveMember(cells[3], members, createPlaceholders, problems);

            if (!int.TryParse(cells[4], NumberStyles.None, CultureInfo.InvariantCulture, out var cups))
                problems.Add($"'{cells[4]}' is not a whole number of cups");

            if (member is not null && !seen.Add((label.ToLowerInvariant(), member.Value.Key)))
                problems.Add($"{member.Value.Name} already has a row for period '{label}'");

            if (problems.Count > 0)
            {
                rowErrors.Add(new("cups", number, line, problems));
                continue;
            }

            cupRows.Add(new CupRow(number, label, member!.Value.UserId, member.Value.NewText, member.Value.Name, cups));
        }

        var purchaseRows = new List<PurchaseRow>();
        var purchaseRecords = Records(purchasesText, "period");
        if (purchaseRecords.Count > MaxRows)
            errors.Add($"At most {MaxRows} purchase rows can be pasted at once");

        foreach (var (line, number) in purchaseRecords.Take(MaxRows))
        {
            var cells = Cells(line);
            var problems = new List<string>();
            if (cells.Length != 6)
            {
                rowErrors.Add(new("purchases", number, line, ["Expected period;date;paidBy;description;grams;cost"]));
                continue;
            }

            var label = cells[0];
            if (!periods.ContainsKey(label))
                problems.Add($"Period '{label}' is not in the cups block");

            DateTime? date = cells[1].Length == 0 ? null : ParseDate(cells[1], "date", problems);

            (string Key, UserId? UserId, string? NewText, string Name)? payer = null;
            if (cells[2].Length > 0)
                payer = ResolveMember(cells[2], members, createPlaceholders, problems);

            var description = cells[3];
            if (description.Length == 0 || description.Length > HistoricalPurchase.MaxDescriptionLength)
                problems.Add($"The description must be 1–{HistoricalPurchase.MaxDescriptionLength} characters");

            decimal? grams = null;
            if (cells[4].Length > 0)
            {
                if (BalancePasteEvaluator.TryParseAmount(cells[4], out var g) && g > 0)
                    grams = g;
                else
                    problems.Add($"'{cells[4]}' is not a positive number of grams");
            }

            if (!BalancePasteEvaluator.TryParseAmount(cells[5], out var cost) || cost <= 0)
                problems.Add($"'{cells[5]}' is not a positive cost");

            if (problems.Count > 0)
            {
                rowErrors.Add(new("purchases", number, line, problems));
                continue;
            }

            purchaseRows.Add(new PurchaseRow(
                number, label, date, payer?.UserId, payer?.NewText, payer?.Name, description, grams, cost));
        }

        if (ledgerStartDate is { } ledgerStart)
        {
            foreach (var period in periods.Values.Where(p => p.EndDate >= ledgerStart.Date))
                errors.Add($"Period '{period.Label}' must end before the ledger start date ({ledgerStart:yyyy-MM-dd})");
        }

        var ordered = periods.Values.OrderBy(p => p.StartDate).ThenBy(p => p.Label, StringComparer.OrdinalIgnoreCase).ToList();
        return new Evaluation(ordered, cupRows, purchaseRows, rowErrors, errors);
    }

    private static (string Key, UserId? UserId, string? NewText, string Name)? ResolveMember(
        string text, MemberResolver members, bool createPlaceholders, List<string> problems)
    {
        var match = members.Resolve(text);
        if (match.UserId is { } userId)
            return (userId.Value.ToString(), userId, null, match.Name!);

        if (match.IsUnknown && createPlaceholders)
        {
            var trimmed = text.Trim();
            return ("new:" + trimmed.ToLowerInvariant(), null, trimmed, trimmed);
        }

        problems.Add(match.Error ?? $"'{text}' is not a member of this space");
        return null;
    }

    private static DateTime? ParseDate(string text, string what, List<string> problems)
    {
        if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return DateTime.SpecifyKind(date, DateTimeKind.Utc);

        problems.Add($"The {what} '{text}' is not a yyyy-MM-dd date");
        return null;
    }

    private static List<(string Line, int Number)> Records(string? text, string headerKeyword)
    {
        var records = (text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select((line, index) => (Line: line.Trim(), Number: index + 1))
            .Where(r => r.Line.Length > 0)
            .ToList();

        if (records.Count > 0 && string.Equals(Cells(records[0].Line)[0], headerKeyword, StringComparison.OrdinalIgnoreCase))
            records.RemoveAt(0);

        return records;
    }

    private static string[] Cells(string line) => line.Split(';').Select(c => c.Trim()).ToArray();
}
