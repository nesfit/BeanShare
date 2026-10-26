namespace BeanShare.Domain.ValueObjects;

public readonly record struct HistoryImportId(Guid Value)
{
    public static HistoryImportId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
