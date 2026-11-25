using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Features.Search;
using Kanabol.Application.Features.Search.Commands.EmbedTasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kanabol.Infrastructure.Embeddings;

/// <summary>
/// Makes the missing and stale vectors at start, once the database is migrated (it's registered after
/// DatabaseInitializer, and hosted services start one after another), then every Embeddings:Interval (SPEC
/// section 19.5), as OverdueTasksWorker does. Each run is EmbedTasksCommand in a scope of its own, without a
/// user, in batches of Embeddings:BatchSize. A run that fails is logged, and the next one goes on from the first
/// task still to do. With Embeddings:Enabled false it does nothing, so it's off by default.
/// </summary>
public sealed class EmbeddingsWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<EmbeddingsOptions> options,
    ILogger<EmbeddingsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var embeddings = options.Value;
        if (!embeddings.Enabled)
        {
            logger.LogInformation("The vectorization is turned off");
            return;
        }

        logger.LogInformation(
            "The vectorization runs now and every {Interval}, in batches of {BatchSize}",
            embeddings.Interval,
            embeddings.BatchSize);
        using var timer = new PeriodicTimer(embeddings.Interval);
        try
        {
            do
            {
                await Run(embeddings.BatchSize, stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The API is stopping, maybe in the middle of a run: the batches saved so far stay.
        }
    }

    private async Task Run(int batchSize, CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<EmbedTasksCommand, EmbeddedTasks>>();
            var result = await handler.Handle(new EmbedTasksCommand(batchSize), ct);
            if (result.IsFailure)
            {
                logger.LogError("The vectorization failed with {ErrorCode}", result.Error.Code);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The vectorization failed");
        }
    }
}
