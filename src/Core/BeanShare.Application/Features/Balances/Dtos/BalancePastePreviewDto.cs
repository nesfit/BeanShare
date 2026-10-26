namespace BeanShare.Application.Features.Balances.Dtos;

public sealed record BalancePasteRowDto(
    int LineNumber,
    string MemberText,
    Guid? UserId,
    string? UserName,
    decimal? Amount,
    string Note,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Preview of a pasted <c>member;amount;note</c> batch. Nothing is saved; <see cref="Errors"/> holds
/// batch-level problems, each row its own.
/// </summary>
public sealed record BalancePastePreviewDto(
    IReadOnlyList<BalancePasteRowDto> Rows,
    IReadOnlyList<string> Errors,
    int ValidRowCount,
    int ErrorRowCount,
    decimal Total,
    string Currency,
    Guid CounterpartyUserId,
    string CounterpartyName,
    DateTime EffectiveDate,
    string Kind,
    decimal CounterpartyBalance,
    decimal CounterpartyResultingBalance)
{
    public bool CanCommit => Errors.Count == 0 && ErrorRowCount == 0 && Rows.Count > 0;
}
