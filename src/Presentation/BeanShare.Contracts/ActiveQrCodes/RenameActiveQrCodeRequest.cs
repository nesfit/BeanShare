namespace BeanShare.Contracts.ActiveQrCodes;

public sealed record RenameActiveQrCodeRequest
{
    public required string Label { get; init; }
}
