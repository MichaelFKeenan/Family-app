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

    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId)
    {
        userId = Guid.Empty;
        return principal.Identity?.IsAuthenticated == true
            && Guid.TryParse(principal.FindFirst(UserId)?.Value, out userId);
    }
}
