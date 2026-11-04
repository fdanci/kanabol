using Kanabol.Application.Abstractions;
using Kanabol.Infrastructure.Persistence;
using Kanabol.Infrastructure.Persistence.Interceptors;
using Kanabol.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<AuditingInterceptor>();
        services.AddScoped<DomainEventsInterceptor>();

        services.AddDbContext<AppDbContext>((provider, options) => options
            .UseNpgsql(configuration.GetConnectionString("Default"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                provider.GetRequiredService<AuditingInterceptor>(),
                provider.GetRequiredService<DomainEventsInterceptor>())

            // Warns that children of an archived task or project can outlive the filter on their parent.
            // That's the plan: they're read through their parent, and they stay when it's archived.
            .ConfigureWarnings(warnings => warnings.Ignore(
                CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)));
        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());

        return services;
    }
}
