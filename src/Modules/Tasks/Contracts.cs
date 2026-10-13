using FamilyApp.Modules.Tasks.Data;

namespace FamilyApp.Modules.Tasks;

public sealed record TaskDto(
    Guid Id,
    string Title,
    string? Description,
    TaskState Status,
    TaskPriority Priority,
    int Points,
    Guid? CategoryId,
    Guid? AssigneeId,
    DateOnly? DueOn,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    Guid? CompletedBy,
    int Version,
    Guid? OwnerId)
{
    internal static TaskDto From(TaskItem task) => new(
        task.Id, task.Title, task.Description, task.Status, task.Priority, task.Points,
        task.CategoryId, task.AssigneeId, task.DueOn, task.CreatedBy, task.CreatedAt,
        task.UpdatedAt, task.CompletedAt, task.CompletedBy, task.Version, task.OwnerId);
}

public sealed record CategoryDto(Guid Id, string Name, int SortOrder)
{
    internal static CategoryDto From(Category category) => new(category.Id, category.Name, category.SortOrder);
}

public sealed record CreateTaskRequest(
    string? Title,
    string? Description,
    TaskPriority? Priority,
    int? Points,
    Guid? CategoryId,
    Guid? AssigneeId,
    DateOnly? DueOn,
    bool? Personal = null);

/// <summary>Which board a task list is for (ADR-037).</summary>
public enum TaskBoard
{
    Family,
    Personal,
}

/// <summary>A full edit. <see cref="Version"/> is the version the client last saw (ADR-014).</summary>
public sealed record UpdateTaskRequest(
    string? Title,
    string? Description,
    TaskPriority? Priority,
    int? Points,
    Guid? CategoryId,
    DateOnly? DueOn,
    int Version);

public sealed record AssignTaskRequest(Guid? AssigneeId);

public sealed record CategoryRequest(string? Name, int? SortOrder);

/// <summary>What a task operation produced, mapped to HTTP by the endpoints.</summary>
public abstract record Outcome<T>
{
    public sealed record Ok(T Value) : Outcome<T>;
    public sealed record NotFound : Outcome<T>;
    public sealed record Conflict(string Message) : Outcome<T>;
    public sealed record Invalid(string Field, string Message) : Outcome<T>;
}
