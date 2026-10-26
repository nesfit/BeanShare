namespace BeanShare.Domain.Common;

/// <summary>
/// Calendar dates of the money ledger (entry dates, the ledger start) are stored as UTC midnight. A
/// Local-kind value is converted to UTC first, so every caller lands on the same day.
/// </summary>
public static class UtcDate
{
    public static DateTime Of(DateTime value) =>
        DateTime.SpecifyKind((value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value).Date, DateTimeKind.Utc);
}
