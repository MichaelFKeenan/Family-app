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

    private readonly List<IDomainEvent> _dispatched = [];

    /// <summary>Every event dispatched in this request, for live updates once it commits (ADR-042).</summary>
    public IReadOnlyList<IDomainEvent> Dispatched => _dispatched;

    public async Task DispatchAsync(CancellationToken cancellationToken)
    {
        while (events.TryDequeue(out var domainEvent))
        {
            _dispatched.Add(domainEvent);
            if (_dispatched.Count > MaxEventsPerRequest)
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
