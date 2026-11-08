using System.Reflection;
using FluentValidation;
using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common.Decorators;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Reports;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services) =>
        services
            .AddScoped<SessionIssuer>()
            .AddScoped<CurrentPermissions>()
            .AddScoped<LastAdminGuard>()
            .AddScoped<IProjectAccess, ProjectAccess>()
            .AddScoped<ProjectChanges>()
            .AddScoped<ArchivedTasksAccess>()
            .AddScoped<TaskChanges>()
            .AddScoped<TaskVisibility>()
            .AddScoped<ReportAccess>()
            .AddHandlersFrom(typeof(DependencyInjection).Assembly);

    public static IServiceCollection AddHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(
                classes => classes.AssignableToAny(
                    typeof(ICommandHandler<,>),
                    typeof(IQueryHandler<,>),
                    typeof(IDomainEventHandler<>)),
                publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(IValidator<>)), publicOnly: false)

            // Not AsImplementedInterfaces: validators also implement IEnumerable<IValidationRule>.
            .As(type => type.GetInterfaces().Where(
                contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IValidator<>)))
            .WithScopedLifetime());

        // Try, because there's nothing to decorate until the first handler exists. The last one
        // added runs first, so logging also sees the commands that validation turns away.
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(ValidationDecorator<,>));
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(LoggingDecorator<,>));
        services.TryDecorate(typeof(IQueryHandler<,>), typeof(QueryValidationDecorator<,>));

        return services;
    }
}
