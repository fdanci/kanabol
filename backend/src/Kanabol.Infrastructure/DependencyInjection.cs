using Kanabol.Application.Abstractions;
using Kanabol.Infrastructure.Alerts;
using Kanabol.Infrastructure.Authentication;
using Kanabol.Infrastructure.Configuration;
using Kanabol.Infrastructure.Persistence;
using Kanabol.Infrastructure.Persistence.Interceptors;
using Kanabol.Infrastructure.Storage;
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
        services.AddValidatedOptions<StorageOptions>(configuration, StorageOptions.SectionName)
            .Validate(
                storage => StorageOptions.ExtensionsIn(storage.AllowedExtensions).Count > 0,
                "Storage:AllowedExtensions needs at least one extension, separated by commas.")

            // Bound to one value, a list by index (__0, __1) would be dropped without a word.
            .Validate(
                _ => !configuration.GetSection($"{StorageOptions.SectionName}:{nameof(StorageOptions.AllowedExtensions)}").GetChildren().Any(),
                "Storage:AllowedExtensions is one value separated by commas, such as Storage__AllowedExtensions=pdf,png, not a list by index.");
        services.AddValidatedOptions<AdminOptions>(configuration, AdminOptions.SectionName)
            .Validate(
                admin => string.IsNullOrEmpty(admin.InitialPassword)
                    || admin.InitialPassword.Length >= AdminOptions.MinimumPasswordLength,
                $"Admin:InitialPassword must have at least {AdminOptions.MinimumPasswordLength} characters.");
        services.AddValidatedOptions<DatabaseOptions>(configuration, DatabaseOptions.SectionName)
            .Configure(options => options.ConnectionString = configuration.GetConnectionString("Default"));
        services.AddValidatedOptions<AlertsOptions>(configuration, AlertsOptions.SectionName)
            .Validate(
                alerts => alerts.OverdueCheckInterval >= AlertsOptions.MinimumInterval,
                $"Alerts:OverdueCheckInterval must be at least {AlertsOptions.MinimumInterval}.")
            .Validate(
                alerts => alerts.OverdueCheckInterval <= AlertsOptions.MaximumInterval,
                $"Alerts:OverdueCheckInterval must be at most {AlertsOptions.MaximumInterval}.");
        services.AddValidatedOptions<AuthOptions>(configuration, AuthOptions.SectionName)
            .Validate(
                auth => auth.RefreshTokenCleanupInterval >= AuthOptions.MinimumInterval,
                $"Auth:RefreshTokenCleanupInterval must be at least {AuthOptions.MinimumInterval}.")
            .Validate(
                auth => auth.RefreshTokenCleanupInterval <= AuthOptions.MaximumInterval,
                $"Auth:RefreshTokenCleanupInterval must be at most {AuthOptions.MaximumInterval}.");

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IAttachmentRules, StorageAttachmentRules>();
        services.AddScoped<AuditingInterceptor>();
        services.AddScoped<DomainEventsInterceptor>();

        services.AddDbContext<AppDbContext>((provider, options) => options
            // A restart of PostgreSQL leaves the pool's connections dead, and the first command on each failed
            // with 57P01: the strategy runs the operation again on another one (SPEC section 5.3).
            .UseNpgsql(configuration.GetConnectionString("Default"), npgsql => npgsql.EnableRetryOnFailure())
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

        // After DatabaseInitializer: hosted services start one after another, so the first runs find the database migrated.
        services.AddHostedService<OverdueTasksWorker>();
        services.AddHostedService<RefreshTokensCleanupWorker>();

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
