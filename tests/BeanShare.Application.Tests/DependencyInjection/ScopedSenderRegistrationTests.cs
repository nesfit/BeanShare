using System.Text.RegularExpressions;
using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BeanShare.Application.Tests.DependencyInjection;

[CollectionDefinition(nameof(ScopedSenderRegistrationTests), DisableParallelization = true)]
public sealed class ScopedSenderCollection;

// T-DI-01: MediatR's interfaces stay MediatR's; IScopedSender is separate, so sending can never recurse.
[Collection(nameof(ScopedSenderRegistrationTests))]
public sealed partial class ScopedSenderRegistrationTests
{
    public sealed record PingQuery : IRequest<string>;

    public sealed class PingHandler : IRequestHandler<PingQuery, string>
    {
        public Task<string> Handle(PingQuery request, CancellationToken cancellationToken) => Task.FromResult("pong");
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddScopedSender();
        services.AddTransient<IRequestHandler<PingQuery, string>, PingHandler>();
        services.AddScoped(_ => Substitute.For<IUserContext>());
        services.AddScoped(_ => Substitute.For<ISpaceRepository>());
        services.AddScoped(_ => Substitute.For<IUnitOfWork>());
        services.AddScoped(_ => Substitute.For<IDomainEventDispatcher>());
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public void MediatRInterfaces_ResolveToMediatRsOwnMediator()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IMediator>().Should().BeOfType<Mediator>();
        scope.ServiceProvider.GetRequiredService<ISender>().Should().BeOfType<Mediator>();
        scope.ServiceProvider.GetRequiredService<IPublisher>().Should().BeOfType<Mediator>();
    }

    [Fact]
    public void ScopedSender_IsItsOwnServicePerScope()
    {
        using var provider = BuildProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var a = first.ServiceProvider.GetRequiredService<IScopedSender>();
        var b = second.ServiceProvider.GetRequiredService<IScopedSender>();

        a.Should().BeOfType<ScopedSender>();
        b.Should().BeOfType<ScopedSender>();
        a.Should().NotBeSameAs(b);
    }

    [Fact(Timeout = 5000)]
    public async Task Send_CompletesWithoutRecursion()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<IScopedSender>();

        var response = await sender.Send(new PingQuery());

        response.Should().Be("pong");
    }

    [Fact]
    public void Sources_NeverInjectOrReRegisterMediatRInterfaces()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "Presentation");

        var injecting = new[] { "BeanShare.BlazorWeb", "BeanShare.SharedUi" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(presentation, project), "*.razor", SearchOption.AllDirectories))
            .Where(file => !IsBuildOutput(file))
            .Where(file => InjectsMediatR().IsMatch(File.ReadAllText(file)))
            .ToList();

        // The MAUI head has no MediatR pipeline; it registers its own IMediator stub.
        var mauiHead = Path.Combine(presentation, "BeanShare.Maui") + Path.DirectorySeparatorChar;
        var registering = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file) && !file.StartsWith(mauiHead, StringComparison.Ordinal))
            .Where(file => RegistersMediatR().IsMatch(File.ReadAllText(file)))
            .ToList();

        injecting.Should().BeEmpty("web components send through IScopedSender");
        registering.Should().BeEmpty("MediatR forwards ISender/IPublisher to IMediator, so re-registering any of them recurses");
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BeanShare.slnf")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("BeanShare.slnf not found above the test output");
    }

    [GeneratedRegex(@"@inject\s+(MediatR\.)?I(Mediator|Sender|Publisher)\b")]
    private static partial Regex InjectsMediatR();

    [GeneratedRegex(@"Add(Scoped|Transient|Singleton)<\s*(MediatR\.)?I(Mediator|Sender|Publisher)\b")]
    private static partial Regex RegistersMediatR();
}
