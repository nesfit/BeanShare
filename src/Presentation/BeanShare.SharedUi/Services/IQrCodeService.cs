namespace BeanShare.SharedUi.Services;

public interface IQrCodeService
{
    /// <summary>
    /// Generates a QR code PNG as a base64 data URI string (data:image/png;base64,...).
    /// Uses v1 JSON payload format.
    /// </summary>
    string GenerateQrCodeBase64(QrConsumptionPayload payload);

    /// <summary>
    /// Generates a v2 QR code PNG (URL-based) as a base64 data URI string.
    /// The QR contains a scannable URL: {baseUrl}/scan/{qrCodeId}
    /// </summary>
    string GenerateQrCodeBase64ForActiveQr(Guid qrCodeId);

    /// <summary>
    /// Generates a QR code PNG (base64 data URI) encoding an arbitrary URL — e.g. a
    /// payment link — so it can be scanned with a phone.
    /// </summary>
    string GenerateQrCodeBase64ForUrl(string url);

    /// <summary>
    /// Parses a raw QR code string into a payload. Returns null if invalid.
    /// Handles both URL format (https://host/scan/{id}) and legacy JSON format.
    /// </summary>
    QrConsumptionPayload? ParseQrPayload(string rawData);
}
