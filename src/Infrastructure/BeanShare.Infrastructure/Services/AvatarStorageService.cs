using BeanShare.Application.Abstractions;
using BeanShare.Domain.Common;
using Microsoft.Extensions.Configuration;

namespace BeanShare.Infrastructure.Services;

public sealed record AvatarUploadResult(bool Success, string? AvatarUrl, string? Error);

public sealed class AvatarStorageService
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];
    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "image/gif", "image/webp"];
    private const long MaxAvatarSizeBytes = 5 * 1024 * 1024;

    private readonly IConfiguration _configuration;
    private readonly IUserRepository _users;

    public AvatarStorageService(IConfiguration configuration, IUserRepository users)
    {
        _configuration = configuration;
        _users = users;
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
        if (!(await _users.GetAccessStatusAsync(new UserId(userId), cancellationToken)).CanAct)
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
}
