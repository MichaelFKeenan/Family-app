namespace FamilyApp.SharedKernel.Events;

// The published events (ADR-031).
//
// Task events carry OwnerId: null for a family task, the owner's id for a personal one (ADR-037).
// Personal events are private: the activity feed shows them only to their owner, the points
// handlers ignore them, and any future reader of the activity log must respect the same rule. They live here, not in the module that raises them, so any
// module can subscribe without referencing the publisher. Treat them as a public contract:
// add fields freely, but don't rename or remove them, because the activity log keeps their JSON forever.

public sealed record HouseholdCreated(Guid HouseholdId, Guid ActorId, string Name) : IDomainEvent;

/// <summary>Someone joined through an invite. <see cref="IDomainEvent.ActorId"/> is the person who joined.</summary>
public sealed record MemberJoined(Guid HouseholdId, Guid ActorId, string Role) : IDomainEvent;

public sealed record TaskCreated(Guid HouseholdId, Guid ActorId, Guid TaskId, string Title, int Points, Guid? AssigneeId, Guid? OwnerId = null) : IDomainEvent;

public sealed record TaskEdited(Guid HouseholdId, Guid ActorId, Guid TaskId, string Title, int Points, Guid? OwnerId = null) : IDomainEvent;

public sealed record TaskAssigned(Guid HouseholdId, Guid ActorId, Guid TaskId, string Title, Guid? AssigneeId, Guid? OwnerId = null) : IDomainEvent;

public sealed record TaskStarted(Guid HouseholdId, Guid ActorId, Guid TaskId, string Title, Guid? OwnerId = null) : IDomainEvent;

/// <summary>
/// The person who pressed "done" is the actor. Points go to the assignee, or to the actor when
/// the task was unassigned (ADR-016).
/// </summary>
public sealed record TaskCompleted(Guid HouseholdId, Guid ActorId, Guid TaskId, string Title, int Points, Guid? AssigneeId, Guid? OwnerId = null) : IDomainEvent;

public sealed record TaskReopened(Guid HouseholdId, Guid ActorId, Guid TaskId, string Title, bool WasDone, Guid? OwnerId = null) : IDomainEvent;

public sealed record TaskDeleted(Guid HouseholdId, Guid ActorId, Guid TaskId, string Title, bool WasDone, Guid? OwnerId = null) : IDomainEvent;

/// <summary>Points added to someone's total. The actor caused it (by completing the task); the recipient earned it.</summary>
public sealed record PointsAwarded(Guid HouseholdId, Guid ActorId, Guid RecipientId, Guid TaskId, string Title, int Amount) : IDomainEvent;

/// <summary>Points taken back because a completed task was reopened or deleted. <see cref="Amount"/> is positive.</summary>
public sealed record PointsReversed(Guid HouseholdId, Guid ActorId, Guid RecipientId, Guid TaskId, string Title, int Amount) : IDomainEvent;

public sealed record ShoppingItemAdded(Guid HouseholdId, Guid ActorId, Guid ItemId, string Name, string? Quantity) : IDomainEvent;

/// <summary>Ticked off: bought, or no longer needed. The row is kept as purchase history (ADR-010).</summary>
public sealed record ShoppingItemChecked(Guid HouseholdId, Guid ActorId, Guid ItemId, string Name) : IDomainEvent;

/// <summary>A tick undone: the item is back on the active list.</summary>
public sealed record ShoppingItemUnchecked(Guid HouseholdId, Guid ActorId, Guid ItemId, string Name) : IDomainEvent;

/// <summary>Taken off the list without being bought, usually a mistake. Only active items can be removed (ADR-035).</summary>
public sealed record ShoppingItemRemoved(Guid HouseholdId, Guid ActorId, Guid ItemId, string Name) : IDomainEvent;

/// <summary>A family task moved to someone's personal board. Visible to the family, who saw the task until now.</summary>
public sealed record TaskMadePersonal(Guid HouseholdId, Guid ActorId, Guid TaskId, string Title) : IDomainEvent;

/// <summary>A personal task moved onto the family board, where it can now earn points (ADR-037).</summary>
public sealed record TaskSharedWithFamily(Guid HouseholdId, Guid ActorId, Guid TaskId, string Title, int Points, Guid? AssigneeId) : IDomainEvent;
