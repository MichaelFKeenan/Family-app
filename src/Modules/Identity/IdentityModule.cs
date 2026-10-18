using System.Security.Claims;
using FamilyApp.Modules.Identity.Data;
using FamilyApp.SharedKernel;
using FamilyApp.SharedKernel.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Npgsql;

namespace FamilyApp.Modules.Identity;

/// <summary>
/// Users, login identities and sign-in (ADR-007, ADR-008, ADR-022).
///
/// Sign-in flow: the browser is sent to Google; Google sends it back to /signin-google with a code;
/// the OpenID Connect handler swaps the code for an ID token and validates it; we find or create our
/// user; the cookie handler then issues our own session cookie holding only our user id and name.
/// Google's tokens are discarded: after sign-in, Google is no longer involved.
/// </summary>
public sealed class IdentityModule : IModule
{
    public const string GoogleScheme = "Google";
    private const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;

    public string Name => "identity";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IdentityModuleDbContext>();
        services.AddScoped<IdentityService>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<AccessTokenService>();

        var authentication = services.AddAuthentication(CookieScheme);

        // Not the default scheme: only endpoints that opt in with .AllowAccessTokens() run it (ADR-039).
        authentication.AddScheme<AuthenticationSchemeOptions, AccessTokenAuthenticationHandler>(AccessTokens.Scheme, _ => { });

        authentication.AddCookie(CookieScheme, options =>
        {
            options.Cookie.Name = "family.session";
            options.Cookie.HttpOnly = true;               // JavaScript can never read it (ADR-008)
            options.Cookie.SameSite = SameSiteMode.Lax;   // not sent on cross-site POSTs, which blocks most CSRF
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // https in production, http locally
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;

            // This is an API, not a page: answer 401/403 instead of redirecting to a login page.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        var google = configuration.GetSection("Authentication:Google");
        if (!string.IsNullOrWhiteSpace(google["ClientId"]))
        {
            authentication.AddOpenIdConnect(GoogleScheme, "Google", options =>
            {
                options.Authority = "https://accounts.google.com";
                options.ClientId = google["ClientId"];
                options.ClientSecret = google["ClientSecret"];
                options.CallbackPath = "/signin-google";
                options.SignInScheme = CookieScheme;

                // Authorization code flow with PKCE (on by default). The code comes back in the query
                // string rather than a cross-site form POST, so the handler's short-lived correlation
                // cookies can stay SameSite=Lax and the flow also works over plain http locally.
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.ResponseMode = OpenIdConnectResponseMode.Query;
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.NonceCookie.SameSite = SameSiteMode.Lax;
                options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");

                options.MapInboundClaims = false;   // keep the raw OIDC claim names: sub, email, name, picture
                options.SaveTokens = false;         // we never call Google APIs, so never keep Google's tokens
                options.GetClaimsFromUserInfoEndpoint = false; // the ID token already has what we need

                options.Events.OnTokenValidated = async context =>
                {
                    var fromGoogle = context.Principal!;
                    var login = new ExternalLogin(
                        Provider: "google",
                        Subject: fromGoogle.FindFirst("sub")?.Value
                            ?? throw new InvalidOperationException("Google ID token has no subject."),
                        Email: fromGoogle.FindFirst("email")?.Value,
                        DisplayName: fromGoogle.FindFirst("name")?.Value,
                        AvatarUrl: fromGoogle.FindFirst("picture")?.Value);

                    var users = context.HttpContext.RequestServices.GetRequiredService<IdentityService>();
                    var user = await users.SignInExternalAsync(login, context.HttpContext.RequestAborted);

                    // Swap Google's identity for ours. Only this goes into the session cookie.
                    context.Principal = CreatePrincipal(user);
                };
            });
        }

        services.AddAuthorization();
    }

    public void MapEndpoints(IEndpointRouteBuilder group)
    {
        // Starts the Google sign-in redirect. A normal link, not a fetch: the whole page navigates away.
        group.MapGet("/login", async (string? returnUrl, IAuthenticationSchemeProvider schemes) =>
        {
            if (await schemes.GetSchemeAsync(GoogleScheme) is null)
            {
                return Results.Problem("Google sign-in is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var properties = new AuthenticationProperties { RedirectUri = IsLocalPath(returnUrl) ? returnUrl! : "/" };
            return Results.Challenge(properties, [GoogleScheme]);
        }).AllowAnonymous();

        group.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieScheme);
            return Results.NoContent();
        }).AllowAnonymous();

        group.MapGet("/me", async (ClaimsPrincipal principal, IdentityService users, CancellationToken cancellationToken) =>
        {
            if (!principal.TryGetUserId(out var userId))
            {
                return Results.Unauthorized();
            }

            // A valid cookie for a user who no longer exists is treated as signed out.
            var user = await users.GetUserAsync(userId, cancellationToken);
            return user is null ? Results.Unauthorized() : Results.Ok(user);
        }).RequireAuthorization();

        // Access tokens for agents (ADR-039). Cookie only, deliberately: a token can never mint,
        // list or revoke tokens. A token belongs to the household the request resolves to.
        var tokens = group.MapGroup("/tokens").RequireHousehold();

        tokens.MapPost("", async (CreateAccessTokenRequest request, ClaimsPrincipal principal, CurrentHousehold household, AccessTokenService service, CancellationToken ct) =>
        {
            var name = request.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > AccessTokenService.MaxNameLength)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = [$"Give the token a name of 1 to {AccessTokenService.MaxNameLength} characters, such as \"laptop Claude Code\"."],
                });
            }

            if (!principal.TryGetUserId(out var userId)) return Results.Unauthorized();
            var created = await service.CreateAsync(userId, household.Id, name, ct);
            return Results.Created($"/api/identity/tokens/{created.Id}", created);
        });

        tokens.MapGet("", async (ClaimsPrincipal principal, CurrentHousehold household, AccessTokenService service, CancellationToken ct) =>
        {
            if (!principal.TryGetUserId(out var userId)) return Results.Unauthorized();
            return Results.Ok(await service.ListAsync(userId, household.Id, ct));
        });

        // A command, not DELETE: the row stays so the list can show when it was revoked (ADR-014).
        tokens.MapPost("/{id:guid}/revoke", async (Guid id, ClaimsPrincipal principal, AccessTokenService service, CancellationToken ct) =>
        {
            if (!principal.TryGetUserId(out var userId)) return Results.Unauthorized();
            return await service.RevokeAsync(userId, id, ct) ? Results.NoContent() : Results.NotFound();
        });
    }

    private static ClaimsPrincipal CreatePrincipal(UserProfile user) =>
        new(new ClaimsIdentity(
            [
                new Claim(UserClaims.UserId, user.Id.ToString()),
                new Claim(UserClaims.DisplayName, user.DisplayName),
            ],
            authenticationType: GoogleScheme,
            nameType: UserClaims.DisplayName,
            roleType: "role"));

    /// <summary>
    /// Only allow redirects back into this site. Without this check, anyone could craft a sign-in link
    /// that bounces the user to a look-alike site afterwards (an open redirect).
    /// </summary>
    internal static bool IsLocalPath(string? url) =>
        !string.IsNullOrEmpty(url)
        && url[0] == '/'
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}
