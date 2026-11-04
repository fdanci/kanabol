using Kanabol.Application.Abstractions;
using Kanabol.Infrastructure.Persistence;
using Kanabol.Infrastructure.Persistence.Interceptors;
using Kanabol.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
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
                provider.GetRequiredService<DomainEventsInterceptor>()));
        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());

        return services;
    }
}
