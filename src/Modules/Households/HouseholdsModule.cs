using System.Security.Claims;
using FamilyApp.Modules.Households.Data;
using FamilyApp.SharedKernel;
using FamilyApp.SharedKernel.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyApp.Modules.Households;

/// <summary>Households, memberships and invites; the tenant boundary (ADR-009, ADR-026, ADR-027).</summary>
public sealed class HouseholdsModule : IModule
{
    private const int MaxNameLength = 100;

    public string Name => "households";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<HouseholdsDbContext>();
        services.AddScoped<HouseholdService>();
        services.AddScoped<IHouseholdMembership, HouseholdMembership>();
    }

    public void MapEndpoints(IEndpointRouteBuilder group)
    {
        // The households I belong to.
        group.MapGet("", async (ClaimsPrincipal principal, HouseholdService households, CancellationToken cancellationToken) =>
        {
            if (!principal.TryGetUserId(out var userId)) return Results.Unauthorized();
            return Results.Ok(await households.ListForUserAsync(userId, cancellationToken));
        }).RequireAuthorization();

        group.MapPost("", async (CreateHouseholdRequest request, ClaimsPrincipal principal, HouseholdService households, CancellationToken cancellationToken) =>
        {
            var name = request.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = [$"Give the household a name of 1 to {MaxNameLength} characters."],
                });
            }

            if (!principal.TryGetUserId(out var userId)) return Results.Unauthorized();
            var household = await households.CreateAsync(userId, name, cancellationToken);
            return Results.Created("/api/households", household);
        }).RequireAuthorization();

        group.MapGet("/members", async (CurrentHousehold current, HouseholdService households, CancellationToken cancellationToken) =>
            Results.Ok(await households.GetMembersAsync(current.Id, cancellationToken)))
            .RequireHousehold()
            .AllowAccessTokens(); // agents need it to pick assignees (ADR-039)

        group.MapPost("/invites", async (CurrentHousehold current, ClaimsPrincipal principal, HouseholdService households, CancellationToken cancellationToken) =>
        {
            if (!principal.TryGetUserId(out var userId)) return Results.Unauthorized();
            var invite = await households.CreateInviteAsync(current.Id, userId, cancellationToken);
            return invite is null
                ? Results.Problem("Only household admins can invite people.", statusCode: StatusCodes.Status403Forbidden)
                : Results.Ok(invite);
        }).RequireHousehold();

        group.MapPost("/invites/{code}/accept", async (string code, ClaimsPrincipal principal, HouseholdService households, CancellationToken cancellationToken) =>
        {
            if (!principal.TryGetUserId(out var userId)) return Results.Unauthorized();
            var result = await households.AcceptInviteAsync(code, userId, cancellationToken);
            return result.Outcome == InviteOutcome.Invalid
                ? Results.Problem("This invite link is invalid, used or expired.", statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(result);
        }).RequireAuthorization();
    }
}

public sealed record CreateHouseholdRequest(string? Name);
