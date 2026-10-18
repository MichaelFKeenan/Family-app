using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;

namespace FamilyApp.SharedKernel;

/// <summary>
/// Personal access tokens let agents call the API (ADR-039). The Identity module validates them;
/// modules only decide which of their endpoints accept them.
/// </summary>
public static class AccessTokens
{
    /// <summary>The authentication scheme for <c>Authorization: Bearer fb_...</c>.</summary>
    public const string Scheme = "AccessToken";

    /// <summary>
    /// Lets this endpoint accept an access token as well as the session cookie.
    /// An allow-list: the cookie is the default scheme, so an endpoint without this never even
    /// looks at a token and answers 401. A new endpoint is closed to agents until someone opts it in.
    /// </summary>
    public static TBuilder AllowAccessTokens<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new AuthorizeAttribute
        {
            AuthenticationSchemes = $"{CookieAuthenticationDefaults.AuthenticationScheme},{Scheme}",
        });
}
