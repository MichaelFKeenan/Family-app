using System.Security.Claims;

namespace FamilyApp.SharedKernel;

/// <summary>
/// The claims every module can rely on for the signed-in user.
/// Only the Identity module creates them; everyone else just reads them.
/// </summary>
public static class UserClaims
{
    /// <summary>Our own user id (a UUIDv7), never the provider's subject.</summary>
    public const string UserId = "sub";

    public const string DisplayName = "name";

    /// <summary>
    /// Only on access-token requests: the one household the token may act in (ADR-039).
    /// When present, it decides the household and the <c>X-Household-Id</c> header is ignored.
    /// </summary>
    public const string HouseholdId = "household";

    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId)
    {
        userId = Guid.Empty;
        return principal.Identity?.IsAuthenticated == true
            && Guid.TryParse(principal.FindFirst(UserId)?.Value, out userId);
    }
}
