using BeanShare.Application.Abstractions;
using BeanShare.Domain.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BeanShare.Infrastructure.Services;

public sealed record AvatarUploadResult(bool Success, string? AvatarUrl, string? Error);

public sealed class AvatarStorageService
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];
    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "image/gif", "image/webp"];
    private const long MaxAvatarSizeBytes = 5 * 1024 * 1024;

    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopes;

    // The access check reads through a short-lived scope: the web profile page keeps this service for its whole
    // circuit, which must not hold a DbContext that long (DS-24, a fresh DbContext per operation).
    public AvatarStorageService(IConfiguration configuration, IServiceScopeFactory scopes)
    {
        _configuration = configuration;
        _scopes = scopes;
    }

    public string GetUploadsRootPath()
    {
        var configuredPath = _configuration.GetValue<string>("UploadsRootPath");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        return Path.Combine(Path.GetTempPath(), "beanshare", "uploads");
    }

    public async Task<AvatarUploadResult> SaveAvatarAsync(
        Guid userId,
        string fileName,
        string? contentType,
        long fileSize,
        Stream source,
        CancellationToken cancellationToken = default)
    {
        // DS-30 for every caller (the API endpoint and the web profile page): only an active, non-placeholder account
        // may replace its photo, and a refused one leaves the stored files untouched.
        if (!await CanActAsync(userId, cancellationToken))
        {
            return new AvatarUploadResult(false, null, "This account cannot change its photo");
        }

        if (fileSize > MaxAvatarSizeBytes)
        {
            return new AvatarUploadResult(false, null, "File size exceeds 5MB limit");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            return new AvatarUploadResult(false, null, "Invalid file format. Allowed: jpg, png, gif, webp");
        }

        var normalizedContentType = contentType?.ToLowerInvariant() ?? string.Empty;
        if (!AllowedContentTypes.Contains(normalizedContentType))
        {
            return new AvatarUploadResult(false, null, "Invalid content type");
        }

        var uploadsPath = Path.Combine(GetUploadsRootPath(), "avatars");
        Directory.CreateDirectory(uploadsPath);

        var storedFileName = $"{userId}{extension}";
        var filePath = Path.Combine(uploadsPath, storedFileName);

        foreach (var allowedExtension in AllowedExtensions)
        {
            var existingPath = Path.Combine(uploadsPath, $"{userId}{allowedExtension}");
            if (File.Exists(existingPath))
            {
                File.Delete(existingPath);
            }
        }

        await using var destination = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(destination, cancellationToken);

        return new AvatarUploadResult(true, $"/uploads/avatars/{storedFileName}", null);
    }

    private async Task<bool> CanActAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        return (await users.GetAccessStatusAsync(new UserId(userId), cancellationToken)).CanAct;
    }
}
