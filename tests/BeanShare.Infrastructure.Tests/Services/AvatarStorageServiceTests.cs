using BeanShare.Application.Abstractions;
using BeanShare.Domain.Common;
using BeanShare.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BeanShare.Infrastructure.Tests.Services;

/// <summary>
/// DS-30 for the photo upload (A10-4): the web profile page writes the file through this service before it sends its
/// profile command, so the service itself refuses an account that cannot act, before any file is touched.
/// </summary>
public sealed class AvatarStorageServiceTests : IDisposable
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "beanshare-avatar-tests-" + Guid.NewGuid());
    private readonly Guid _user = Guid.NewGuid();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private int _repositoryResolutions;

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task AnAccountThatCannotAct_IsRefused_AndItsPhotoStays(bool exists, bool isActive, bool isPlaceholder)
    {
        var existing = ExistingPhoto(".jpg");
        Access(new UserAccessStatus(exists, isActive, isPlaceholder, null));

        var result = await SaveAsync("me.png");

        result.Success.Should().BeFalse();
        result.AvatarUrl.Should().BeNull();
        File.Exists(existing).Should().BeTrue("a refused upload deletes nothing");
        File.Exists(Path.Combine(_root, "avatars", $"{_user}.png")).Should().BeFalse("and writes nothing");
    }

    [Fact]
    public async Task AnActiveAccount_ReplacesItsPhoto()
    {
        var existing = ExistingPhoto(".jpg");
        Access(new UserAccessStatus(true, true, false, null));

        var result = await SaveAsync("me.png");

        result.Success.Should().BeTrue();
        result.AvatarUrl.Should().Be($"/uploads/avatars/{_user}.png");
        File.Exists(existing).Should().BeFalse();
        File.ReadAllBytes(Path.Combine(_root, "avatars", $"{_user}.png")).Should().Equal(Png);
    }

    // CR14-2: the service holds no repository; each upload reads the access status through its own short-lived scope.
    [Fact]
    public async Task TheAccessCheck_UsesAFreshScopePerUpload()
    {
        Access(new UserAccessStatus(true, true, false, null));
        var service = Service();
        _repositoryResolutions.Should().Be(0);

        (await SaveAsync("me.png", service)).Success.Should().BeTrue();
        (await SaveAsync("me.png", service)).Success.Should().BeTrue();

        _repositoryResolutions.Should().Be(2);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void Access(UserAccessStatus status) =>
        _users.GetAccessStatusAsync(new UserId(_user), Arg.Any<CancellationToken>()).Returns(status);

    private string ExistingPhoto(string extension)
    {
        var path = Path.Combine(_root, "avatars", $"{_user}{extension}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    private AvatarStorageService Service()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["UploadsRootPath"] = _root })
            .Build();
        var services = new ServiceCollection();
        services.AddScoped(_ =>
        {
            _repositoryResolutions++;
            return _users;
        });
        var scopes = services.BuildServiceProvider(validateScopes: true).GetRequiredService<IServiceScopeFactory>();
        return new AvatarStorageService(configuration, scopes);
    }

    private async Task<AvatarUploadResult> SaveAsync(string fileName, AvatarStorageService? service = null)
    {
        using var source = new MemoryStream(Png);
        return await (service ?? Service()).SaveAvatarAsync(_user, fileName, "image/png", Png.Length, source);
    }
}
