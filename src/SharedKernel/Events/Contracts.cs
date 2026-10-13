namespace FamilyApp.SharedKernel.Events;

// The published events (ADR-031). They live here, not in the module that raises them, so any
// module can subscribe without referencing the publisher. Treat them as a public contract:
// add fields freely, but don't rename or remove them, because the activity log keeps their JSON forever.

public sealed record HouseholdCreated(Guid HouseholdId, Guid ActorId, string Name) : IDomainEvent;

/// <summary>Someone joined through an invite. <see cref="IDomainEvent.ActorId"/> is the person who joined.</summary>
public sealed record MemberJoined(Guid HouseholdId, Guid ActorId, string Role) : IDomainEvent;

public sealed record TaskCreated(Guid HouseholdId, Guid ActorId, Guid TaskId, string Title, int Points, Guid? AssigneeId) : IDomainEvent;

public sealed record TaskEdited(Guid HouseholdId, Guid ActorId, Guid TaskId, string Title, int Points) : IDomainEvent;

public sealed record TaskAssigned(Guid HouseholdId, Guid ActorId, Guid TaskId, string Title, Guid? AssigneeId) : IDomainEvent;

public sealed record TaskStarted(Guid HouseholdId, Guid ActorId, Guid TaskId, string Title) : IDomainEvent;

/// <summary>
/// The person who pressed "done" is the actor. Points go to the assignee, or to the actor when
/// the task was unassigned (ADR-016).
/// </summary>
public sealed record TaskCompleted(Guid HouseholdId, Guid ActorId, Guid TaskId, string Title, int Points, Guid? AssigneeId) : IDomainEvent;

public sealed record TaskReopened(Guid HouseholdId, Guid ActorId, Guid TaskId, string Title, bool WasDone) : IDomainEvent;

public sealed record TaskDeleted(Guid HouseholdId, Guid ActorId, Guid TaskId, string Title, bool WasDone) : IDomainEvent;

/// <summary>Points added to someone's total. The actor caused it (by completing the task); the recipient earned it.</summary>
public sealed record PointsAwarded(Guid HouseholdId, Guid ActorId, Guid RecipientId, Guid TaskId, string Title, int Amount) : IDomainEvent;

/// <summary>Points taken back because a completed task was reopened or deleted. <see cref="Amount"/> is positive.</summary>
public sealed record PointsReversed(Guid HouseholdId, Guid ActorId, Guid RecipientId, Guid TaskId, string Title, int Amount) : IDomainEvent;
