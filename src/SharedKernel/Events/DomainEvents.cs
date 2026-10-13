using System.Diagnostics.CodeAnalysis;

namespace FamilyApp.SharedKernel.Events;

/// <summary>
/// Something that happened, named in the past tense. Every event belongs to a household and
/// was caused by someone, which is what the activity log records (ADR-005).
/// </summary>
public interface IDomainEvent
{
    Guid HouseholdId { get; }
    Guid ActorId { get; }
}

/// <summary>
/// The events raised during this request. A module calls <see cref="Raise"/> when it changes
/// something; nothing runs yet. After the endpoint succeeds, the host dispatches them inside
/// the request transaction, then commits (ADR-031).
/// </summary>
public sealed class DomainEvents
{
    private readonly Queue<IDomainEvent> _pending = new();

    public void Raise(IDomainEvent domainEvent) => _pending.Enqueue(domainEvent);

    public bool TryDequeue([NotNullWhen(true)] out IDomainEvent? domainEvent) => _pending.TryDequeue(out domainEvent);

    public int PendingCount => _pending.Count;
}

/// <summary>Non-generic view the dispatcher uses; implement <see cref="DomainEventHandler{TEvent}"/> instead.</summary>
public interface IDomainEventHandler
{
    Type EventType { get; }

    Task HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Reacts to one event type. Register with <c>services.AddScoped&lt;IDomainEventHandler, MyHandler&gt;()</c>.
/// Runs inside the same transaction as the change that raised the event: if a handler throws,
/// the whole request rolls back.
/// </summary>
public abstract class DomainEventHandler<TEvent> : IDomainEventHandler where TEvent : IDomainEvent
{
    public Type EventType => typeof(TEvent);

    Task IDomainEventHandler.HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken) =>
        HandleAsync((TEvent)domainEvent, cancellationToken);

    public abstract Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
