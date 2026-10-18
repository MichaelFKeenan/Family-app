using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyApp.SharedKernel;

/// <summary>
/// Read-only question any module may ask; the Households module answers it (ADR-025).
/// Modules depend on this interface, never on the Households project.
/// </summary>
public interface IHouseholdMembership
{
    Task<IReadOnlyList<Guid>> GetHouseholdIdsAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// The household the current request acts on, set by <see cref="HouseholdEndpoints.RequireHousehold{TBuilder}"/>.
/// Scoped per request. Module DbContexts read it to filter every query (ADR-009).
/// </summary>
public sealed class CurrentHousehold
{
    private Guid? _id;

    public bool IsResolved => _id.HasValue;

    public Guid Id => _id
        ?? throw new InvalidOperationException("No household was resolved for this request. Add .RequireHousehold() to the endpoint.");

    internal void Set(Guid id) => _id = id;
}

/// <summary>
/// The signed-in user of the current request, set by <see cref="HouseholdEndpoints.RequireHousehold{TBuilder}"/>
/// with the household. Module DbContexts read it for per-user rules, such as personal tasks being
/// visible only to their owner (ADR-037). Unresolved reads as <see cref="Guid.Empty"/>, which matches
/// no one, so a query outside a household request sees no personal data: it fails closed.
/// </summary>
public sealed class CurrentUser
{
    public Guid Id { get; private set; }

    internal void Set(Guid id) => Id = id;
}

public static class HouseholdEndpoints
{
    /// <summary>Optional request header naming the household to act on (ADR-026).</summary>
    public const string HeaderName = "X-Household-Id";

    /// <summary>
    /// Requires a signed-in member of a household, and resolves which one:
    /// an access token's own household, else the <see cref="HeaderName"/> header if sent (it must be
    /// one of the user's households), otherwise the user's only household. Membership is checked on every request, so
    /// removing someone takes effect immediately.
    /// </summary>
    public static TBuilder RequireHousehold<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization();
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (!http.User.TryGetUserId(out var userId))
            {
                return Results.Unauthorized();
            }

            var households = await http.RequestServices
                .GetRequiredService<IHouseholdMembership>()
                .GetHouseholdIdsAsync(userId, http.RequestAborted);

            Guid householdId;
            if (Guid.TryParse(http.User.FindFirst(UserClaims.HouseholdId)?.Value, out householdId))
            {
                // An access token is bound to one household (ADR-039). Membership is still checked,
                // so the token stops working the moment its user leaves.
                if (!households.Contains(householdId))
                {
                    return Results.Problem("You are not a member of this token's household.", statusCode: StatusCodes.Status403Forbidden);
                }
            }
            else if (http.Request.Headers.TryGetValue(HeaderName, out var header))
            {
                if (!Guid.TryParse(header.ToString(), out householdId))
                {
                    return Results.Problem($"{HeaderName} must be a household id.", statusCode: StatusCodes.Status400BadRequest);
                }
                if (!households.Contains(householdId))
                {
                    return Results.Problem("You are not a member of that household.", statusCode: StatusCodes.Status403Forbidden);
                }
            }
            else if (households.Count == 1)
            {
                householdId = households[0];
            }
            else if (households.Count == 0)
            {
                return Results.Problem("You are not in a household yet.", statusCode: StatusCodes.Status409Conflict);
            }
            else
            {
                return Results.Problem(
                    $"You belong to several households; send the {HeaderName} header to choose one.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            http.RequestServices.GetRequiredService<CurrentHousehold>().Set(householdId);
            http.RequestServices.GetRequiredService<CurrentUser>().Set(userId);
            return await next(context);
        });
        return builder;
    }
}
