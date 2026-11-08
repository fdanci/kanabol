using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Features.Notifications.Commands.NotifyOverdueTasks;
using Kanabol.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kanabol.Infrastructure.Alerts;

/// <summary>
/// Looks for overdue tasks at start, once the database is migrated (it's registered after
/// DatabaseInitializer, and hosted services start one after another), then every
/// Alerts:OverdueCheckInterval (SPEC section 4.3). Each run is NotifyOverdueTasksCommand in a scope of its
/// own, without a user. A run that fails is logged, and the next one still comes.
/// </summary>
public sealed class OverdueTasksWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AlertsOptions> options,
    ILogger<OverdueTasksWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var alerts = options.Value;
        if (!alerts.OverdueCheckEnabled)
        {
            logger.LogInformation("The overdue check is turned off");
            return;
        }

        logger.LogInformation("The overdue check runs now and every {OverdueCheckInterval}", alerts.OverdueCheckInterval);
        using var timer = new PeriodicTimer(alerts.OverdueCheckInterval);
        try
        {
            do
            {
                await Run(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The API is stopping.
        }
    }

    private async Task Run(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<NotifyOverdueTasksCommand, int>>();
            var result = await handler.Handle(new NotifyOverdueTasksCommand(), ct);
            if (result.IsFailure)
            {
                logger.LogError("The overdue check failed with {ErrorCode}", result.Error.Code);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The overdue check failed");
        }
    }
}
