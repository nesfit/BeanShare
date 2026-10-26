namespace BeanShare.Domain.ValueObjects;

public readonly record struct BalanceEntryId(Guid Value)
{
    public static BalanceEntryId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
