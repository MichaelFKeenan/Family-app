using System.Security.Claims;
using FamilyApp.Modules.Tasks.Data;
using FamilyApp.SharedKernel;
using FamilyApp.SharedKernel.Events;
using FamilyApp.SharedKernel.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyApp.Modules.Tasks;

/// <summary>The board: tasks and categories, all scoped to the current household (ADR-009, ADR-011, ADR-014).</summary>
public sealed class TasksModule : IModule
{
    public string Name => "tasks";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<TasksDbContext>();
        services.AddScoped<TaskService>();
        services.AddScoped<CategoryService>();
        services.AddScoped<IDomainEventHandler, SeedDefaultCategories>();
    }

    public void MapEndpoints(IEndpointRouteBuilder group)
    {
        // Every endpoint here acts on the current household; resolving it happens once, here.
        var tasks = group.MapGroup("").RequireHousehold();

        // status is bound as a string: minimal APIs parse query-string enums case-sensitively,
        // and the API's enums are lower-case ("todo"), so we parse it ourselves.
        tasks.MapGet("", async (string? status, Guid? categoryId, Guid? assigneeId, TaskService service, CancellationToken ct) =>
        {
            TaskState? state = null;
            if (status is not null)
            {
                if (!Enum.TryParse<TaskState>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["status"] = ["Status must be todo, doing or done."],
                    });
                }
                state = parsed;
            }

            return Results.Ok(await service.ListAsync(state, categoryId, assigneeId, ct));
        });

        tasks.MapGet("/{id:guid}", async (Guid id, TaskService service, CancellationToken ct) =>
            ToHttp(await service.GetAsync(id, ct)));

        tasks.MapPost("", async (CreateTaskRequest request, ClaimsPrincipal user, TaskService service, CancellationToken ct) =>
        {
            user.TryGetUserId(out var userId);
            var outcome = await service.CreateAsync(request, userId, ct);
            return outcome is Outcome<TaskDto>.Ok created
                ? Results.Created($"/api/tasks/{created.Value.Id}", created.Value)
                : ToHttp(outcome);
        });

        tasks.MapPut("/{id:guid}", async (Guid id, UpdateTaskRequest request, ClaimsPrincipal user, TaskService service, CancellationToken ct) =>
        {
            user.TryGetUserId(out var userId);
            return ToHttp(await service.UpdateAsync(id, request, userId, ct));
        });

        tasks.MapPost("/{id:guid}/assign", async (Guid id, AssignTaskRequest request, ClaimsPrincipal user, TaskService service, CancellationToken ct) =>
        {
            user.TryGetUserId(out var userId);
            return ToHttp(await service.AssignAsync(id, request.AssigneeId, userId, ct));
        });

        tasks.MapPost("/{id:guid}/start", async (Guid id, ClaimsPrincipal user, TaskService service, CancellationToken ct) =>
        {
            user.TryGetUserId(out var userId);
            return ToHttp(await service.StartAsync(id, userId, ct));
        });

        tasks.MapPost("/{id:guid}/complete", async (Guid id, ClaimsPrincipal user, TaskService service, CancellationToken ct) =>
        {
            user.TryGetUserId(out var userId);
            return ToHttp(await service.CompleteAsync(id, userId, ct));
        });

        tasks.MapPost("/{id:guid}/reopen", async (Guid id, ClaimsPrincipal user, TaskService service, CancellationToken ct) =>
        {
            user.TryGetUserId(out var userId);
            return ToHttp(await service.ReopenAsync(id, userId, ct));
        });

        tasks.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, TaskService service, CancellationToken ct) =>
        {
            user.TryGetUserId(out var userId);
            return await service.DeleteAsync(id, userId, ct) ? Results.NoContent() : Results.NotFound();
        });

        // Categories live under the tasks module because only tasks use them.
        var categories = tasks.MapGroup("/categories");

        categories.MapGet("", (CategoryService service, CancellationToken ct) => service.ListAsync(ct));

        categories.MapPost("", async (CategoryRequest request, CategoryService service, CancellationToken ct) =>
            ToHttp(await service.CreateAsync(request, ct)));

        categories.MapPut("/{id:guid}", async (Guid id, CategoryRequest request, CategoryService service, CancellationToken ct) =>
            ToHttp(await service.UpdateAsync(id, request, ct)));

        categories.MapDelete("/{id:guid}", async (Guid id, CategoryService service, CancellationToken ct) =>
            await service.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());
    }

    private static IResult ToHttp<T>(Outcome<T> outcome) => outcome switch
    {
        Outcome<T>.Ok ok => Results.Ok(ok.Value),
        Outcome<T>.NotFound => Results.NotFound(),
        Outcome<T>.Conflict conflict => Results.Problem(conflict.Message, statusCode: StatusCodes.Status409Conflict),
        Outcome<T>.Invalid invalid => Results.ValidationProblem(
            new Dictionary<string, string[]> { [invalid.Field] = [invalid.Message] },
            detail: invalid.Message),
        _ => throw new InvalidOperationException($"Unhandled outcome {outcome.GetType().Name}."),
    };
}
