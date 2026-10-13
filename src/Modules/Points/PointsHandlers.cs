using FamilyApp.Modules.Points.Data;
using FamilyApp.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;

namespace FamilyApp.Modules.Points;

/// <summary>
/// Completing a task awards its points (ADR-006, ADR-016). Runs in the same transaction as the
/// completion (ADR-030): if this fails, the task isn't completed either.
/// </summary>
internal sealed class AwardPointsOnCompletion(PointsDbContext db, DomainEvents events, TimeProvider clock)
    : DomainEventHandler<TaskCompleted>
{
    public override async Task HandleAsync(TaskCompleted completed, CancellationToken cancellationToken)
    {
        if (completed.Points <= 0) return;

        // The assignee earns the points; an unassigned task pays whoever completed it.
        var recipient = completed.AssigneeId ?? completed.ActorId;

        db.Ledger.Add(new LedgerEntry
        {
            Id = Guid.CreateVersion7(),
            HouseholdId = completed.HouseholdId,
            UserId = recipient,
            TaskId = completed.TaskId,
            Amount = completed.Points,
            Reason = LedgerReasons.TaskCompleted,
            Title = completed.Title,
            OccurredAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);

        events.Raise(new PointsAwarded(completed.HouseholdId, completed.ActorId, recipient, completed.TaskId, completed.Title, completed.Points));
    }
}

/// <summary>
/// Reopening or deleting a completed task takes its points back. Works from the ledger, not the
/// task: whatever each person currently holds for this task is reversed. That stays correct even
/// if the task was reassigned or its points edited after it was completed.
/// </summary>
internal sealed class ReversePoints(PointsDbContext db, DomainEvents events, TimeProvider clock)
    : DomainEventHandler<TaskReopened>
{
    public override Task HandleAsync(TaskReopened reopened, CancellationToken cancellationToken) =>
        reopened.WasDone
            ? Reverse(db, events, clock, reopened.HouseholdId, reopened.ActorId, reopened.TaskId, reopened.Title, LedgerReasons.TaskReopened, cancellationToken)
            : Task.CompletedTask;

    internal static async Task Reverse(
        PointsDbContext db,
        DomainEvents events,
        TimeProvider clock,
        Guid householdId,
        Guid actorId,
        Guid taskId,
        string title,
        string reason,
        CancellationToken cancellationToken)
    {
        var owed = await db.Ledger
            .Where(e => e.HouseholdId == householdId && e.TaskId == taskId)
            .GroupBy(e => e.UserId)
            .Select(g => new { UserId = g.Key, Net = g.Sum(e => e.Amount) })
            .Where(x => x.Net != 0)
            .ToListAsync(cancellationToken);

        if (owed.Count == 0) return;

        var now = clock.GetUtcNow();
        foreach (var holding in owed)
        {
            db.Ledger.Add(new LedgerEntry
            {
                Id = Guid.CreateVersion7(),
                HouseholdId = householdId,
                UserId = holding.UserId,
                TaskId = taskId,
                Amount = -holding.Net,
                Reason = reason,
                Title = title,
                OccurredAt = now,
            });
        }
        await db.SaveChangesAsync(cancellationToken);

        foreach (var holding in owed)
        {
            events.Raise(new PointsReversed(householdId, actorId, holding.UserId, taskId, title, holding.Net));
        }
    }
}

internal sealed class ReversePointsOnDelete(PointsDbContext db, DomainEvents events, TimeProvider clock)
    : DomainEventHandler<TaskDeleted>
{
    public override Task HandleAsync(TaskDeleted deleted, CancellationToken cancellationToken) =>
        deleted.WasDone
            ? ReversePoints.Reverse(db, events, clock, deleted.HouseholdId, deleted.ActorId, deleted.TaskId, deleted.Title, LedgerReasons.TaskDeleted, cancellationToken)
            : Task.CompletedTask;
}
