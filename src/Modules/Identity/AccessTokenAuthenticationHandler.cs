using System.Security.Claims;
using System.Text.Encodings.Web;
using FamilyApp.Modules.Identity.Data;
using FamilyApp.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyApp.Modules.Identity;

/// <summary>
/// Turns <c>Authorization: Bearer fb_...</c> into the signed-in user, like the session cookie does (ADR-039).
/// Runs only on endpoints that call <c>.AllowAccessTokens()</c>.
/// </summary>
public sealed class AccessTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>
    /// Writing last_used_at on every request would turn every agent read into a database write.
    /// "Last used" only needs to be roughly right, so it is written at most this often.
    /// </summary>
    private static readonly TimeSpan LastUsedPrecision = TimeSpan.FromMinutes(1);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // NoResult means "not mine": the request didn't bring a token of ours, so the cookie can still answer.
        // Fail is for a token in our format that doesn't work: unknown, revoked or expired.
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = header[BearerPrefix.Length..].Trim();
        if (!token.StartsWith(AccessTokenFormat.Prefix, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        var services = Context.RequestServices;
        var db = services.GetRequiredService<IdentityModuleDbContext>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var hash = AccessTokenFormat.Hash(token);

        var found = await db.AccessTokens.AsNoTracking()
            .Where(t => t.TokenHash == hash)
            .Join(db.Users, t => t.UserId, u => u.Id, (t, u) => new
            {
                t.Id,
                t.UserId,
                t.HouseholdId,
                t.ExpiresAt,
                t.RevokedAt,
                t.LastUsedAt,
                u.DisplayName,
            })
            .SingleOrDefaultAsync(Context.RequestAborted);

        // One vague message for all three, so a caller can't probe which tokens exist.
        if (found is null || found.RevokedAt is not null || found.ExpiresAt <= now)
        {
            return AuthenticateResult.Fail("The access token is unknown, revoked or expired.");
        }

        if (found.LastUsedAt is null || found.LastUsedAt < now - LastUsedPrecision)
        {
            // Outside the request transaction on purpose: it runs on GETs too, and a write request that
            // later rolls back should still count as a use. The WHERE keeps concurrent requests from
            // all writing.
            var cutoff = now - LastUsedPrecision;
            await db.AccessTokens
                .Where(t => t.Id == found.Id && (t.LastUsedAt == null || t.LastUsedAt < cutoff))
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.LastUsedAt, now), Context.RequestAborted);
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(UserClaims.UserId, found.UserId.ToString()),
                new Claim(UserClaims.DisplayName, found.DisplayName),
                new Claim(UserClaims.HouseholdId, found.HouseholdId.ToString()),
            ],
            authenticationType: Scheme.Name,
            nameType: UserClaims.DisplayName,
            roleType: "role"));

        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
