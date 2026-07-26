using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using QRCoder;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace BeanShare.SharedUi.Services;

public sealed partial class QrCodeService : IQrCodeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    private static readonly Lazy<byte[]?> LogoBytes = new(() => LoadEmbeddedLogo());
    private static readonly ConcurrentDictionary<Guid, string> _qrImageCache = new();
    private const int MaxQrImageCacheSize = 500;

    private readonly string _scanBaseUrl;

    public QrCodeService(string scanBaseUrl = "http://localhost:5126")
    {
        _scanBaseUrl = scanBaseUrl.TrimEnd('/');
    }

    public string GenerateQrCodeBase64(QrConsumptionPayload payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return GenerateQrWithLogo(json);
    }

    public string GenerateQrCodeBase64ForActiveQr(Guid qrCodeId)
    {
        if (_qrImageCache.TryGetValue(qrCodeId, out var cached))
        {
            return cached;
        }

        // Evict oldest half when cache grows too large
        if (_qrImageCache.Count > MaxQrImageCacheSize)
        {
            var keysToRemove = _qrImageCache.Keys.Take(_qrImageCache.Count / 2).ToList();
            foreach (var key in keysToRemove)
            {
                _qrImageCache.TryRemove(key, out _);
            }
        }

        var url = $"{_scanBaseUrl}/scan/{qrCodeId}";
        var result = GenerateQrWithLogo(url);
        _qrImageCache[qrCodeId] = result;
        return result;
    }

    public string GenerateQrCodeBase64ForUrl(string url)
    {
        return GenerateQrWithLogo(url ?? string.Empty);
    }

    public QrConsumptionPayload? ParseQrPayload(string rawData)
    {
        if (string.IsNullOrWhiteSpace(rawData))
            return null;

        // Try URL format first: .../scan/{guid}
        var qrCodeId = TryExtractQrCodeIdFromUrl(rawData);
        if (qrCodeId.HasValue)
        {
            return new QrConsumptionPayload
            {
                Version = 2,
                QrCodeId = qrCodeId.Value
            };
        }

        try
        {
            var payload = JsonSerializer.Deserialize<QrConsumptionPayload>(rawData, JsonOptions);

            if (payload is null)
                return null;

            if (payload.Version >= 2)
            {
                if (payload.QrCodeId is null || payload.QrCodeId == Guid.Empty)
                    return null;

                return payload;
            }

            if (payload.SpaceId == Guid.Empty || string.IsNullOrEmpty(payload.ProductName))
                return null;

            return payload;
        }
        catch
        {
            return null;
        }
    }

    private static Guid? TryExtractQrCodeIdFromUrl(string rawData)
    {
        var match = ScanUrlRegex().Match(rawData);
        if (match.Success && Guid.TryParse(match.Groups[1].Value, out var id))
            return id;
        return null;
    }

    [GeneratedRegex(@"/scan/([0-9a-fA-F\-]{36})(?:[/?#]|$)", RegexOptions.None)]
    private static partial Regex ScanUrlRegex();

    private static string GenerateQrWithLogo(string content)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(content, QRCodeGenerator.ECCLevel.H);
        var qrCode = new PngByteQRCode(qrCodeData);
        var qrPngBytes = qrCode.GetGraphic(10);

        var logoData = LogoBytes.Value;
        if (logoData == null || logoData.Length == 0)
        {
            var base64 = Convert.ToBase64String(qrPngBytes);
            return $"data:image/png;base64,{base64}";
        }

        using var qrImage = Image.Load<Rgba32>(qrPngBytes);
        using var logo = Image.Load<Rgba32>(logoData);

        var logoSize = (int)(qrImage.Width * 0.18);
        logo.Mutate(ctx => ctx.Resize(logoSize, logoSize));

        var cx = (qrImage.Width - logoSize) / 2;
        var cy = (qrImage.Height - logoSize) / 2;
        var padding = 6;

        // White background behind logo
        for (var py = cy - padding; py < cy + logoSize + padding; py++)
        for (var px = cx - padding; px < cx + logoSize + padding; px++)
        {
            if (px >= 0 && px < qrImage.Width && py >= 0 && py < qrImage.Height)
                qrImage[px, py] = new Rgba32(255, 255, 255, 255);
        }

        // Draw logo
        for (var ly = 0; ly < logo.Height; ly++)
        for (var lx = 0; lx < logo.Width; lx++)
        {
            var pixel = logo[lx, ly];
            if (pixel.A > 0)
                qrImage[cx + lx, cy + ly] = pixel;
        }

        using var ms = new MemoryStream();
        qrImage.SaveAsPng(ms);
        var resultBase64 = Convert.ToBase64String(ms.ToArray());
        return $"data:image/png;base64,{resultBase64}";
    }

    private static byte[]? LoadEmbeddedLogo()
    {
        try
        {
            var assembly = typeof(QrCodeService).Assembly;
            using var stream = assembly.GetManifestResourceStream("BeanShare.SharedUi.qr-logo.png");
            if (stream == null) return null;

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[BeanShare] Failed to load QR logo: {ex.Message}");
#endif
            return null;
        }
    }
}
