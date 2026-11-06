using Kanabol.Application.Abstractions;
using Kanabol.Infrastructure.Authentication;
using Kanabol.Infrastructure.Configuration;
using Kanabol.Infrastructure.Persistence;
using Kanabol.Infrastructure.Persistence.Interceptors;
using Kanabol.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kanabol.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<JwtOptions>(configuration, JwtOptions.SectionName);
        services.AddValidatedOptions<StorageOptions>(configuration, StorageOptions.SectionName);
        services.AddValidatedOptions<AdminOptions>(configuration, AdminOptions.SectionName)
            .Validate(
                admin => string.IsNullOrEmpty(admin.InitialPassword)
                    || admin.InitialPassword.Length >= AdminOptions.MinimumPasswordLength,
                $"Admin:InitialPassword must have at least {AdminOptions.MinimumPasswordLength} characters.");
        services.AddValidatedOptions<DatabaseOptions>(configuration, DatabaseOptions.SectionName)
            .Configure(options => options.ConnectionString = configuration.GetConnectionString("Default"));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
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
        services.AddScoped<ITaskNumbers, TaskNumbers>();
        services.AddScoped<DatabaseSeeder>();
        services.AddHostedService<DatabaseInitializer>();

        return services;
    }

    private static OptionsBuilder<TOptions> AddValidatedOptions<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        where TOptions : class =>
        services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
}
