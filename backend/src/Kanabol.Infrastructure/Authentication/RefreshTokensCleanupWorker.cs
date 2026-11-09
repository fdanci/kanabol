using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Features.Auth.Commands.DeleteOldRefreshTokens;
using Kanabol.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kanabol.Infrastructure.Authentication;

/// <summary>
/// Deletes the old refresh tokens at start, once the database is migrated (it's registered after
/// DatabaseInitializer, and hosted services start one after another), then every
/// Auth:RefreshTokenCleanupInterval (SPEC section 4.5), as OverdueTasksWorker does. Each run is
/// DeleteOldRefreshTokensCommand in a scope of its own, without a user. A run that fails is logged, and the
/// next one still comes.
/// </summary>
public sealed class RefreshTokensCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AuthOptions> options,
    ILogger<RefreshTokensCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var auth = options.Value;
        if (!auth.RefreshTokenCleanupEnabled)
        {
            logger.LogInformation("The refresh tokens' cleanup is turned off");
            return;
        }

        logger.LogInformation("The refresh tokens' cleanup runs now and every {RefreshTokenCleanupInterval}", auth.RefreshTokenCleanupInterval);
        using var timer = new PeriodicTimer(auth.RefreshTokenCleanupInterval);
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
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteOldRefreshTokensCommand, int>>();
            var result = await handler.Handle(new DeleteOldRefreshTokensCommand(), ct);
            if (result.IsFailure)
            {
                logger.LogError("The refresh tokens' cleanup failed with {ErrorCode}", result.Error.Code);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The refresh tokens' cleanup failed");
        }
    }
}
