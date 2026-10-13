using FamilyApp.SharedKernel.Events;

namespace FamilyApp.Host.Events;

/// <summary>
/// Runs the events raised during a request (ADR-004, ADR-031): each one is written to the
/// activity log, then passed to every handler registered for its type. Handlers may raise
/// further events, which are processed in the same pass. Everything happens inside the
/// request transaction, so it all commits or none of it does.
/// </summary>
public sealed class DomainEventDispatcher(
    DomainEvents events,
    IEnumerable<IDomainEventHandler> handlers,
    ActivityLog activityLog,
    ILogger<DomainEventDispatcher> logger)
{
    // A handler that raises an event that triggers itself would loop forever.
    private const int MaxEventsPerRequest = 100;

    public async Task DispatchAsync(CancellationToken cancellationToken)
    {
        var dispatched = 0;
        while (events.TryDequeue(out var domainEvent))
        {
            if (++dispatched > MaxEventsPerRequest)
            {
                throw new InvalidOperationException(
                    $"More than {MaxEventsPerRequest} events in one request; a handler is probably re-raising its own event.");
            }

            await activityLog.RecordAsync(domainEvent, cancellationToken);

            foreach (var handler in handlers.Where(h => h.EventType == domainEvent.GetType()))
            {
                logger.LogDebug("Handling {Event} with {Handler}", domainEvent.GetType().Name, handler.GetType().Name);
                await handler.HandleAsync(domainEvent, cancellationToken);
            }
        }
    }
}
