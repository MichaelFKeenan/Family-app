using FamilyApp.Modules.Identity.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FamilyApp.Modules.Identity;

/// <summary>What an external provider (Google today) tells us about the person signing in.</summary>
public sealed record ExternalLogin(
    string Provider,
    string Subject,
    string? Email,
    string? DisplayName,
    string? AvatarUrl);

/// <summary>The signed-in user, as the rest of the app sees them.</summary>
public sealed record UserProfile(
    Guid Id,
    string DisplayName,
    string? Email,
    string? AvatarUrl);

public sealed class IdentityService(IdentityModuleDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Finds the user behind an external login, creating them on first sign-in.
    /// The profile is refreshed from the provider on every sign-in.
    /// </summary>
    public async Task<UserProfile> SignInExternalAsync(ExternalLogin login, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();

        var identity = await db.UserIdentities.SingleOrDefaultAsync(
            i => i.Provider == login.Provider && i.ProviderSubject == login.Subject,
            cancellationToken);

        if (identity is not null)
        {
            var existing = await db.Users.SingleAsync(u => u.Id == identity.UserId, cancellationToken);
            existing.DisplayName = DisplayNameFor(login);
            existing.Email = login.Email;
            existing.AvatarUrl = login.AvatarUrl;
            existing.LastSeenAt = now;
            identity.Email = login.Email;

            await db.SaveChangesAsync(cancellationToken);
            return ToProfile(existing);
        }

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            DisplayName = DisplayNameFor(login),
            Email = login.Email,
            AvatarUrl = login.AvatarUrl,
            CreatedAt = now,
            LastSeenAt = now,
        };

        db.Users.Add(user);
        db.UserIdentities.Add(new UserIdentity
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Provider = login.Provider,
            ProviderSubject = login.Subject,
            Email = login.Email,
            CreatedAt = now,
        });

        try
        {
            // One SaveChanges = one transaction: the user and its identity appear together or not at all.
            await db.SaveChangesAsync(cancellationToken);
            return ToProfile(user);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The same person signed in twice at once (two tabs, a double tap) and the other request
            // created the user first. The unique constraint on (provider, provider_subject) caught it;
            // discard our attempt and use theirs.
            db.ChangeTracker.Clear();
            return await SignInExternalAsync(login, cancellationToken);
        }
    }

    public async Task<UserProfile?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return user is null ? null : ToProfile(user);
    }

    private static string DisplayNameFor(ExternalLogin login) =>
        !string.IsNullOrWhiteSpace(login.DisplayName) ? login.DisplayName
        : !string.IsNullOrWhiteSpace(login.Email) ? login.Email
        : "Family member";

    private static UserProfile ToProfile(User user) =>
        new(user.Id, user.DisplayName, user.Email, user.AvatarUrl);
}
