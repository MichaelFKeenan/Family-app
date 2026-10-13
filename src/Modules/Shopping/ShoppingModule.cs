using System.Security.Claims;
using FamilyApp.Modules.Shopping.Data;
using FamilyApp.SharedKernel;
using FamilyApp.SharedKernel.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyApp.Modules.Shopping;

/// <summary>The permanent shopping list (ADR-010, ADR-035), scoped to the current household.</summary>
public sealed class ShoppingModule : IModule
{
    public string Name => "shopping";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ShoppingDbContext>();
        services.AddScoped<ShoppingService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder group)
    {
        var shopping = group.MapGroup("").RequireHousehold();

        // The active list: everything not yet ticked, oldest first.
        shopping.MapGet("", (ShoppingService service, CancellationToken ct) => service.ActiveAsync(ct));

        // Recently ticked items, newest first, so a mistaken tick can be undone.
        shopping.MapGet("/checked", (int? limit, ShoppingService service, CancellationToken ct) =>
            service.RecentlyCheckedAsync(limit ?? 20, ct));

        // 201 when the item was added, 200 when it was already on the list (nothing changed).
        shopping.MapPost("", async (AddItemRequest request, ClaimsPrincipal user, ShoppingService service, CancellationToken ct) =>
        {
            user.TryGetUserId(out var userId);
            var outcome = await service.AddAsync(request, userId, ct);
            return outcome is Outcome<AddItemResult>.Ok { Value.AlreadyOnList: false } added
                ? Results.Created($"/api/shopping/{added.Value.Item.Id}", added.Value)
                : ToHttp(outcome);
        });

        shopping.MapPost("/{id:guid}/check", async (Guid id, ClaimsPrincipal user, ShoppingService service, CancellationToken ct) =>
        {
            user.TryGetUserId(out var userId);
            return ToHttp(await service.CheckAsync(id, userId, CheckReason.Bought, ct));
        });

        // Off the list without buying it: kept in history, but not counted as a purchase (ADR-038).
        // A separate command rather than a body on /check, like the task commands (ADR-014).
        shopping.MapPost("/{id:guid}/not-needed", async (Guid id, ClaimsPrincipal user, ShoppingService service, CancellationToken ct) =>
        {
            user.TryGetUserId(out var userId);
            return ToHttp(await service.CheckAsync(id, userId, CheckReason.NotNeeded, ct));
        });

        shopping.MapPost("/{id:guid}/uncheck", async (Guid id, ClaimsPrincipal user, ShoppingService service, CancellationToken ct) =>
        {
            user.TryGetUserId(out var userId);
            return ToHttp(await service.UncheckAsync(id, userId, ct));
        });

        shopping.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, ShoppingService service, CancellationToken ct) =>
        {
            user.TryGetUserId(out var userId);
            var outcome = await service.RemoveAsync(id, userId, ct);
            return outcome is Outcome<bool>.Ok ? Results.NoContent() : ToHttp(outcome);
        });
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
