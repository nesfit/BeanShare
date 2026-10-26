using BeanShare.Domain.Enums;

namespace BeanShare.Api.Endpoints.Balances;

/// <summary>Parses the entry kind of a request ("Opening" / "Correction"); empty means Opening.</summary>
internal static class BalanceEntryKinds
{
    public static bool TryParse(string? text, out BalanceEntryKind kind)
    {
        kind = BalanceEntryKind.Opening;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        return Enum.TryParse(text, ignoreCase: true, out kind) && Enum.IsDefined(kind);
    }
}
