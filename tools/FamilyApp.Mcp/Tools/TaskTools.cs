using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace FamilyApp.Mcp.Tools;

/// <summary>
/// Task board tools: the family board (shared, earns points) and the caller's personal board
/// (private, no points), ADR-037. State changes are separate commands, like the API (ADR-014). No delete.
/// </summary>
[McpServerToolType]
public sealed class TaskTools(FamilyApi api)
{
    [McpServerTool(Name = "list_tasks", ReadOnly = true, UseStructuredContent = true)]
    [Description("Tasks on the family board or on the user's own personal board. Returns each task's version, " +
                 "which edit_task needs. Check the board before creating tasks, so you don't add one that exists.")]
    public Task<BoardTask[]> ListTasks(
        [Description("\"family\" (shared, earns points) or \"personal\" (only the user sees it). Default family.")] string board = "family",
        [Description("Only tasks in this state: \"todo\", \"doing\" or \"done\". Leave out for all.")] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = $"/api/tasks?board={Uri.EscapeDataString(board)}";
        if (status is not null) query += $"&status={Uri.EscapeDataString(status)}";
        return api.GetAsync<BoardTask[]>(query, cancellationToken);
    }

    [McpServerTool(Name = "create_task", UseStructuredContent = true)]
    [Description("Creates a task in the to do column. On the family board, whoever completes it earns its points " +
                 "(the assignee if there is one). Personal tasks are private to the user and earn no points.")]
    public Task<BoardTask> CreateTask(
        [Description("Short and specific, as it should appear on the board: \"Take the bins out\". Up to 200 characters.")] string title,
        [Description("Extra detail, only if the user gave some.")] string? description = null,
        [Description("\"low\", \"medium\", \"high\" or \"urgent\". Default medium.")] string? priority = null,
        [Description("Points for completing it, 0 to 1000. Use 0 on personal tasks.")] int? points = null,
        [Description("A category id from list_categories, or leave out.")] Guid? categoryId = null,
        [Description("A member's userId from list_members. On a personal task, only the user themselves.")] Guid? assigneeId = null,
        [Description("Due date as yyyy-mm-dd, only when the user named a day.")] DateOnly? dueOn = null,
        [Description("true for the user's private personal board; false or left out for the family board.")] bool? personal = null,
        CancellationToken cancellationToken = default) =>
        api.PostAsync<BoardTask>("/api/tasks", new { title, description, priority, points, categoryId, assigneeId, dueOn, personal }, cancellationToken);

    [McpServerTool(Name = "edit_task", UseStructuredContent = true)]
    [Description("Changes a task's details. Send only the fields that change; the rest keep their values. " +
                 "To clear the description, category or due date, pass an empty string. A stale version is " +
                 "refused: list the tasks again and retry with the current version.")]
    public async Task<BoardTask> EditTask(
        [Description("The task's id, from list_tasks.")] Guid id,
        [Description("The task's version, from the list_tasks result you based this edit on.")] int version,
        string? title = null,
        [Description("New description, or \"\" to clear it.")] string? description = null,
        [Description("\"low\", \"medium\", \"high\" or \"urgent\".")] string? priority = null,
        [Description("0 to 1000.")] int? points = null,
        [Description("A category id from list_categories, or \"\" to clear it.")] string? categoryId = null,
        [Description("yyyy-mm-dd, or \"\" to clear it.")] string? dueOn = null,
        CancellationToken cancellationToken = default)
    {
        // The API's edit replaces every field, so start from the task as it is now and change only what was sent.
        // The version stays the caller's: if someone changed the task since the agent read it, the API says so.
        var current = await api.GetAsync<BoardTask>($"/api/tasks/{id}", cancellationToken);
        return await api.PutAsync<BoardTask>($"/api/tasks/{id}", new
        {
            title = title ?? current.Title,
            description = description is null ? current.Description : Blank(description),
            priority = priority ?? current.Priority,
            points = points ?? current.Points,
            categoryId = categoryId is null ? current.CategoryId : ParseOrClear<Guid>(categoryId, "categoryId"),
            dueOn = dueOn is null ? current.DueOn : ParseOrClear<DateOnly>(dueOn, "dueOn"),
            version,
        }, cancellationToken);
    }

    [McpServerTool(Name = "assign_task", Idempotent = true, UseStructuredContent = true)]
    [Description("Assigns a task to a household member, or unassigns it. On the family board the assignee earns the points.")]
    public Task<BoardTask> AssignTask(
        [Description("The task's id, from list_tasks.")] Guid id,
        [Description("A member's userId from list_members, or leave out to unassign.")] Guid? assigneeId = null,
        CancellationToken cancellationToken = default) =>
        api.PostAsync<BoardTask>($"/api/tasks/{id}/assign", new { assigneeId }, cancellationToken);

    [McpServerTool(Name = "start_task", UseStructuredContent = true)]
    [Description("Moves a task to the doing column.")]
    public Task<BoardTask> StartTask([Description("The task's id, from list_tasks.")] Guid id, CancellationToken cancellationToken = default) =>
        api.PostAsync<BoardTask>($"/api/tasks/{id}/start", null, cancellationToken);

    [McpServerTool(Name = "complete_task", UseStructuredContent = true)]
    [Description("Moves a task to done. On the family board this awards its points to the assignee, or to the user " +
                 "if nobody is assigned, so only complete tasks the user says are actually done.")]
    public Task<BoardTask> CompleteTask([Description("The task's id, from list_tasks.")] Guid id, CancellationToken cancellationToken = default) =>
        api.PostAsync<BoardTask>($"/api/tasks/{id}/complete", null, cancellationToken);

    [McpServerTool(Name = "reopen_task", UseStructuredContent = true)]
    [Description("Moves a task back to to do. If it was done, the points it awarded are taken back.")]
    public Task<BoardTask> ReopenTask([Description("The task's id, from list_tasks.")] Guid id, CancellationToken cancellationToken = default) =>
        api.PostAsync<BoardTask>($"/api/tasks/{id}/reopen", null, cancellationToken);

    [McpServerTool(Name = "list_categories", ReadOnly = true, UseStructuredContent = true)]
    [Description("The household's task categories, in display order. Use their ids in create_task and edit_task.")]
    public Task<Category[]> ListCategories(CancellationToken cancellationToken) =>
        api.GetAsync<Category[]>("/api/tasks/categories", cancellationToken);

    [McpServerTool(Name = "list_members", ReadOnly = true, UseStructuredContent = true)]
    [Description("The people in the household, with the userId to use when assigning tasks.")]
    public Task<Member[]> ListMembers(CancellationToken cancellationToken) =>
        api.GetAsync<Member[]>("/api/households/members", cancellationToken);

    private static string? Blank(string value) => value.Length == 0 ? null : value;

    private static T? ParseOrClear<T>(string value, string argument) where T : struct, IParsable<T>
    {
        if (value.Length == 0) return null;
        return T.TryParse(value, null, out var parsed)
            ? parsed
            : throw new McpException($"{argument} must be {(typeof(T) == typeof(DateOnly) ? "a date as yyyy-mm-dd" : "an id")}, or \"\" to clear it.");
    }
}

/// <summary>A task as the API returns it. OwnerId is set when it's on someone's personal board.</summary>
public sealed record BoardTask(
    Guid Id,
    string Title,
    string? Description,
    string Status,
    string Priority,
    int Points,
    Guid? CategoryId,
    Guid? AssigneeId,
    DateOnly? DueOn,
    DateTimeOffset? CompletedAt,
    Guid? CompletedBy,
    int Version,
    Guid? OwnerId);

public sealed record Category(Guid Id, string Name, int SortOrder);

public sealed record Member(Guid UserId, string DisplayName, string Role);
