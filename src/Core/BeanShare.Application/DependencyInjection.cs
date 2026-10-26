using BeanShare.Application.Behaviors;
using BeanShare.Application.Common;
using FluentValidation;
using Mapster;
using MapsterMapper;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace BeanShare.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddValidatorsFromAssembly(assembly);

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));

        // Add Mapster
        var config = TypeAdapterConfig.GlobalSettings;
        config.Scan(assembly);
        services.AddSingleton(config);
        services.AddScoped<IMapper, ServiceMapper>();

        services.AddScoped<Features.Balances.Services.LedgerSnapshotService>();

        // First: an identity without an active, real user row must not reach anything else (DS-30).
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(CurrentUserBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(Pipeline.AuthorizationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ConcurrencyRetryBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(UnitOfWorkBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ExceptionHandlingBehavior<,>));

        return services;
    }

    /// <summary>
    /// Registers <see cref="IScopedSender"/> for hosts whose callers outlive a request (Blazor circuits).
    /// MediatR's own IMediator/ISender/IPublisher registrations stay untouched.
    /// </summary>
    public static IServiceCollection AddScopedSender(this IServiceCollection services)
    {
        services.AddScoped<IScopedSender, ScopedSender>();
        return services;
    }
}
