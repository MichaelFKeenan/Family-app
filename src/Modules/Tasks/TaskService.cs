using FamilyApp.Modules.Tasks.Data;
using FamilyApp.SharedKernel;
using FamilyApp.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;

namespace FamilyApp.Modules.Tasks;

/// <summary>
/// Task use cases. Each state change is its own operation (start, complete, reopen, assign) rather
/// than a generic "set status", so each enforces its own rule and raises exactly one event (ADR-004).
/// Events are only queued here; the host dispatches them before the request commits (ADR-031).
/// </summary>
public sealed class TaskService(
    TasksDbContext db,
    CurrentHousehold household,
    IHouseholdMembership membership,
    DomainEvents events,
    TimeProvider clock)
{
    public const int MaxTitle = 200;
    public const int MaxDescription = 2000;
    public const int MaxPoints = 1000;

    public async Task<IReadOnlyList<TaskDto>> ListAsync(
        TaskState? status, Guid? categoryId, Guid? assigneeId, CancellationToken cancellationToken)
    {
        // No household WHERE clause here: the global query filter adds it.
        var query = db.Tasks.AsNoTracking();
        if (status is not null) query = query.Where(t => t.Status == status);
        if (categoryId is not null) query = query.Where(t => t.CategoryId == categoryId);
        if (assigneeId is not null) query = query.Where(t => t.AssigneeId == assigneeId);

        var tasks = await query.OrderBy(t => t.CreatedAt).ToListAsync(cancellationToken);

        // Most urgent first. Done in memory: the priority column is text, so SQL would sort it alphabetically.
        return tasks
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.DueOn ?? DateOnly.MaxValue)
            .Select(TaskDto.From)
            .ToList();
    }

    public async Task<Outcome<TaskDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var task = await db.Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        return task is null ? new Outcome<TaskDto>.NotFound() : new Outcome<TaskDto>.Ok(TaskDto.From(task));
    }

    public async Task<Outcome<TaskDto>> CreateAsync(CreateTaskRequest request, Guid userId, CancellationToken cancellationToken)
    {
        var problem = ValidateFields(request.Title, request.Description, request.Points)
            ?? await ValidateCategoryAsync(request.CategoryId, cancellationToken)
            ?? await ValidateAssigneeAsync(request.AssigneeId, cancellationToken);
        if (problem is not null) return problem;

        var now = clock.GetUtcNow();
        var task = new TaskItem
        {
            Id = Guid.CreateVersion7(),
            Title = request.Title!.Trim(),
            Description = Clean(request.Description),
            Status = TaskState.Todo,
            Priority = request.Priority ?? TaskPriority.Medium,
            Points = request.Points ?? 0,
            CategoryId = request.CategoryId,
            AssigneeId = request.AssigneeId,
            DueOn = request.DueOn,
            CreatedBy = userId,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
            // HouseholdId is stamped by TasksDbContext.SaveChangesAsync.
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);

        events.Raise(new TaskCreated(task.HouseholdId, userId, task.Id, task.Title, task.Points, task.AssigneeId));
        return new Outcome<TaskDto>.Ok(TaskDto.From(task));
    }

    public async Task<Outcome<TaskDto>> UpdateAsync(Guid id, UpdateTaskRequest request, Guid userId, CancellationToken cancellationToken)
    {
        var problem = ValidateFields(request.Title, request.Description, request.Points)
            ?? await ValidateCategoryAsync(request.CategoryId, cancellationToken);
        if (problem is not null) return problem;

        return await ChangeAsync(id, request.Version, task =>
        {
            task.Title = request.Title!.Trim();
            task.Description = Clean(request.Description);
            task.Priority = request.Priority ?? task.Priority;
            task.Points = request.Points ?? task.Points;
            task.CategoryId = request.CategoryId;
            task.DueOn = request.DueOn;
            return null;
        }, task => new TaskEdited(task.HouseholdId, userId, task.Id, task.Title, task.Points), cancellationToken);
    }

    public async Task<Outcome<TaskDto>> AssignAsync(Guid id, Guid? assigneeId, Guid userId, CancellationToken cancellationToken)
    {
        var problem = await ValidateAssigneeAsync(assigneeId, cancellationToken);
        if (problem is not null) return problem;

        return await ChangeAsync(id, expectedVersion: null, task =>
        {
            task.AssigneeId = assigneeId;
            return null;
        }, task => new TaskAssigned(task.HouseholdId, userId, task.Id, task.Title, task.AssigneeId), cancellationToken);
    }

    public Task<Outcome<TaskDto>> StartAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        ChangeAsync(id, expectedVersion: null, task =>
        {
            if (task.Status != TaskState.Todo) return $"Only a to-do task can be started; this one is {Describe(task.Status)}.";
            task.Status = TaskState.Doing;
            return null;
        }, task => new TaskStarted(task.HouseholdId, userId, task.Id, task.Title), cancellationToken);

    public Task<Outcome<TaskDto>> CompleteAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        ChangeAsync(id, expectedVersion: null, task =>
        {
            // Refusing a second completion matters: completion awards points (ADR-006, ADR-016).
            if (task.Status == TaskState.Done) return "This task is already done.";
            task.Status = TaskState.Done;
            task.CompletedAt = clock.GetUtcNow();
            task.CompletedBy = userId;
            return null;
        }, task => new TaskCompleted(task.HouseholdId, userId, task.Id, task.Title, task.Points, task.AssigneeId), cancellationToken);

    public async Task<Outcome<TaskDto>> ReopenAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var wasDone = false;
        return await ChangeAsync(id, expectedVersion: null, task =>
        {
            if (task.Status == TaskState.Todo) return "This task is already to do.";
            wasDone = task.Status == TaskState.Done;
            task.Status = TaskState.Todo;
            task.CompletedAt = null;
            task.CompletedBy = null;
            return null;
        }, task => new TaskReopened(task.HouseholdId, userId, task.Id, task.Title, wasDone), cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        // Loaded rather than deleted in bulk, because the event needs to say what was deleted.
        var task = await db.Tasks.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (task is null) return false;

        db.Tasks.Remove(task);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return false; // Someone else changed or deleted it first.
        }

        events.Raise(new TaskDeleted(task.HouseholdId, userId, task.Id, task.Title, task.Status == TaskState.Done));
        return true;
    }

    /// <summary>
    /// Load, apply a change, save with the version check, then queue the event. <paramref name="expectedVersion"/>
    /// is the version the client saw (edits); for commands it is null and the loaded version is used, which
    /// still makes two simultaneous commands on one task conflict instead of both succeeding.
    /// </summary>
    private async Task<Outcome<TaskDto>> ChangeAsync(
        Guid id,
        int? expectedVersion,
        Func<TaskItem, string?> change,
        Func<TaskItem, IDomainEvent> happened,
        CancellationToken cancellationToken)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (task is null) return new Outcome<TaskDto>.NotFound();

        if (expectedVersion is not null)
        {
            if (expectedVersion != task.Version) return StaleConflict();
            // Make the UPDATE check the version the client saw, not just the one we loaded.
            db.Entry(task).Property(t => t.Version).OriginalValue = expectedVersion.Value;
        }

        var refusal = change(task);
        if (refusal is not null) return new Outcome<TaskDto>.Conflict(refusal);

        task.Version++;
        task.UpdatedAt = clock.GetUtcNow();

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return StaleConflict();
        }

        events.Raise(happened(task));
        return new Outcome<TaskDto>.Ok(TaskDto.From(task));
    }

    private static Outcome<TaskDto> StaleConflict() =>
        new Outcome<TaskDto>.Conflict("Someone else changed this task. Reload it and try again.");

    private static Outcome<TaskDto>? ValidateFields(string? title, string? description, int? points)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > MaxTitle)
            return new Outcome<TaskDto>.Invalid("title", $"Give the task a title of 1 to {MaxTitle} characters.");
        if (description?.Length > MaxDescription)
            return new Outcome<TaskDto>.Invalid("description", $"Keep the description under {MaxDescription} characters.");
        if (points is < 0 or > MaxPoints)
            return new Outcome<TaskDto>.Invalid("points", $"Points must be between 0 and {MaxPoints}.");
        return null;
    }

    private async Task<Outcome<TaskDto>?> ValidateCategoryAsync(Guid? categoryId, CancellationToken cancellationToken)
    {
        // The query filter means another household's category simply isn't found here.
        if (categoryId is null || await db.Categories.AnyAsync(c => c.Id == categoryId, cancellationToken)) return null;
        return new Outcome<TaskDto>.Invalid("categoryId", "That category doesn't exist in your household.");
    }

    private async Task<Outcome<TaskDto>?> ValidateAssigneeAsync(Guid? assigneeId, CancellationToken cancellationToken)
    {
        if (assigneeId is null) return null;
        // Membership belongs to the Households module; ask it through the SharedKernel contract (ADR-025).
        var theirHouseholds = await membership.GetHouseholdIdsAsync(assigneeId.Value, cancellationToken);
        return theirHouseholds.Contains(household.Id)
            ? null
            : new Outcome<TaskDto>.Invalid("assigneeId", "You can only assign tasks to members of your household.");
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Describe(TaskState status) => status switch
    {
        TaskState.Doing => "already in progress",
        TaskState.Done => "done",
        _ => "to do",
    };
}
