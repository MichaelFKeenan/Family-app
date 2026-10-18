using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using FamilyApp.Modules.Identity.Data;
using Microsoft.EntityFrameworkCore;

namespace FamilyApp.Modules.Identity;

/// <summary>A token as its owner sees it in the list. Never includes the token itself.</summary>
public sealed record AccessTokenSummary(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);

/// <summary>Returned once, at creation: the only time the token itself leaves the server.</summary>
public sealed record CreatedAccessToken(Guid Id, string Name, string Token, DateTimeOffset ExpiresAt);

public sealed record CreateAccessTokenRequest(string? Name);

/// <summary>What a token looks like, and how it is stored (ADR-039).</summary>
public static class AccessTokenFormat
{
    /// <summary>Lets secret scanners (GitHub's included) recognise a leaked token.</summary>
    public const string Prefix = "fb_";

    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    /// <summary><c>fb_</c> plus 32 random bytes, base64url-encoded: 46 characters, safe in headers and env vars.</summary>
    public static string Generate() => Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>What the database stores and looks tokens up by.</summary>
    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}

/// <summary>Create, list and revoke tokens. Only ever called with the session cookie, never a token.</summary>
public sealed class AccessTokenService(IdentityModuleDbContext db, TimeProvider clock)
{
    public const int MaxNameLength = 100;

    public async Task<CreatedAccessToken> CreateAsync(Guid userId, Guid householdId, string name, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var token = AccessTokenFormat.Generate();
        var entity = new AccessToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            HouseholdId = householdId,
            Name = name,
            TokenHash = AccessTokenFormat.Hash(token),
            CreatedAt = now,
            ExpiresAt = now + AccessTokenFormat.Lifetime,
        };

        db.AccessTokens.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return new CreatedAccessToken(entity.Id, entity.Name, token, entity.ExpiresAt);
    }

    /// <summary>The user's tokens for one household, newest first, revoked and expired ones included.</summary>
    public async Task<IReadOnlyList<AccessTokenSummary>> ListAsync(Guid userId, Guid householdId, CancellationToken cancellationToken) =>
        await db.AccessTokens.AsNoTracking()
            .Where(t => t.UserId == userId && t.HouseholdId == householdId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new AccessTokenSummary(t.Id, t.Name, t.CreatedAt, t.ExpiresAt, t.LastUsedAt, t.RevokedAt))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// False when the user has no such token. Revoking twice is fine and keeps the first time,
    /// like ticking a shopping item twice (ADR-035).
    /// </summary>
    public async Task<bool> RevokeAsync(Guid userId, Guid tokenId, CancellationToken cancellationToken)
    {
        var token = await db.AccessTokens.SingleOrDefaultAsync(t => t.Id == tokenId && t.UserId == userId, cancellationToken);
        if (token is null)
        {
            return false;
        }

        token.RevokedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
