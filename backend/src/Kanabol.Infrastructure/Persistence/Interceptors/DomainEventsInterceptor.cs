using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kanabol.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Publishes the events raised by saved entities once SaveChanges has committed. A handler that
/// fails is logged and skipped: the data is already committed, so failing the save would be wrong.
/// A save inside a transaction of its caller commits nothing yet, so its events wait for the first
/// save after that transaction (TaskNumbers makes one right after its commit).
/// </summary>
public sealed class DomainEventsInterceptor(
    IServiceProvider services,
    ILogger<DomainEventsInterceptor> logger) : SaveChangesInterceptor
{
    private List<Entity> _pending = [];

    private interface IPublisher
    {
        Task Publish(
            IDomainEvent domainEvent,
            IServiceProvider services,
            ILogger logger,
            CancellationToken ct);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (!InsideATransaction(eventData.Context))
        {
            PublishPending().GetAwaiter().GetResult();
        }

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (!InsideATransaction(eventData.Context))
        {
            await PublishPending();
        }

        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => _pending = [];

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _pending = [];
        return Task.CompletedTask;
    }

    // EF's own transaction is over by the time a save ends, so one still open is the caller's.
    private static bool InsideATransaction(DbContext? context) => context?.Database.CurrentTransaction is not null;

    // Entities are collected before saving because a deleted one leaves the tracker during the save.
    // Those of earlier saves in the same transaction are still waiting, so they stay.
    private void Collect(DbContext? context)
    {
        if (context is not null)
        {
            _pending = [.. _pending.Union(context.ChangeTracker.Entries<Entity>()
                .Select(entry => entry.Entity)
                .Where(entity => entity.DomainEvents.Count > 0))];
        }
    }

    private async Task PublishPending()
    {
        var entities = _pending;
        _pending = [];

        var events = entities.SelectMany(entity => entity.DomainEvents).ToList();
        entities.ForEach(entity => entity.ClearDomainEvents());

        foreach (var domainEvent in events)
        {
            var publisher = (IPublisher)Activator.CreateInstance(
                typeof(Publisher<>).MakeGenericType(domainEvent.GetType()))!;

            // Not the request's token: the change is committed, so a client that hung up
            // mustn't keep other users from hearing about it.
            await publisher.Publish(domainEvent, services, logger, CancellationToken.None);
        }
    }

    private sealed class Publisher<TEvent> : IPublisher
        where TEvent : IDomainEvent
    {
        public async Task Publish(
            IDomainEvent domainEvent,
            IServiceProvider services,
            ILogger logger,
            CancellationToken ct)
        {
            foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
            {
                try
                {
                    await handler.Handle((TEvent)domainEvent, ct);
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Handler {Handler} failed on domain event {DomainEvent}",
                        handler.GetType().Name,
                        typeof(TEvent).Name);
                }
            }
        }
    }
}
