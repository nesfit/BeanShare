using System.Collections.Concurrent;

namespace BeanShare.SharedUi.Services;

/// <summary>
/// In-memory cache for resolved Active QR code data.
/// Falls back to cached results when the API/server is unreachable.
/// </summary>
public sealed class QrCodeResolveCache
{
    private readonly record struct Entry(QrConsumptionPayload Payload, DateTimeOffset StoredAt);

    private readonly ConcurrentDictionary<Guid, Entry> _cache = new();
    private const int MaxCacheSize = 200;

    // Bounds how stale an offline-fallback resolution can be. A QR that is reassigned,
    // renamed, or deactivated on another head cannot invalidate this in-process cache, so
    // a TTL is the only cross-head-safe guard against serving stale product/label data.
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    public void Store(Guid qrCodeId, QrConsumptionPayload resolved)
    {
        if (_cache.Count > MaxCacheSize)
        {
            _cache.Clear();
        }

        _cache[qrCodeId] = new Entry(resolved, DateTimeOffset.UtcNow);
    }

    public QrConsumptionPayload? TryGet(Guid qrCodeId)
    {
        if (_cache.TryGetValue(qrCodeId, out var entry))
        {
            if (DateTimeOffset.UtcNow - entry.StoredAt <= Ttl)
            {
                return entry.Payload;
            }

            _cache.TryRemove(qrCodeId, out _);
        }

        return null;
    }

    public void Remove(Guid qrCodeId) => _cache.TryRemove(qrCodeId, out _);
}
