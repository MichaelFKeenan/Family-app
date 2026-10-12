using System.Text.Json.Serialization;
using FamilyApp.Modules.Households.Data;
using FamilyApp.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace FamilyApp.Modules.Households;

public sealed record HouseholdSummary(Guid Id, string Name, string Role);

public sealed record HouseholdMember(Guid UserId, string DisplayName, string? AvatarUrl, string Role, DateTimeOffset JoinedAt);

public sealed record CreatedInvite(string Code, DateTimeOffset ExpiresAt);

[JsonConverter(typeof(JsonStringEnumConverter<InviteOutcome>))]
public enum InviteOutcome
{
    Joined,
    AlreadyMember,
    Invalid,
}

public sealed record AcceptInviteResult(InviteOutcome Outcome, Guid? HouseholdId);

public sealed class HouseholdService(HouseholdsDbContext db, IUserDirectory users, TimeProvider clock)
{
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

    public async Task<HouseholdSummary> CreateAsync(Guid userId, string name, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var household = new Household
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            CreatedBy = userId,
            CreatedAt = now,
        };

        db.Households.Add(household);
        db.Memberships.Add(new Membership
        {
            HouseholdId = household.Id,
            UserId = userId,
            Role = Membership.Admin,
            JoinedAt = now,
        });

        await db.SaveChangesAsync(cancellationToken);
        return new HouseholdSummary(household.Id, household.Name, Membership.Admin);
    }

    public async Task<IReadOnlyList<HouseholdSummary>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await (
            from membership in db.Memberships
            join household in db.Households on membership.HouseholdId equals household.Id
            where membership.UserId == userId
            orderby membership.JoinedAt
            select new HouseholdSummary(household.Id, household.Name, membership.Role))
        .AsNoTracking()
        .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<HouseholdMember>> GetMembersAsync(Guid householdId, CancellationToken cancellationToken)
    {
        var memberships = await db.Memberships
            .AsNoTracking()
            .Where(m => m.HouseholdId == householdId)
            .OrderBy(m => m.JoinedAt)
            .ToListAsync(cancellationToken);

        // Names live in the Identity module; ask it through the SharedKernel contract (ADR-025)
        // rather than joining its tables.
        var profiles = await users.GetUsersAsync(memberships.Select(m => m.UserId).ToList(), cancellationToken);

        return memberships
            .Select(m => profiles.TryGetValue(m.UserId, out var profile)
                ? new HouseholdMember(m.UserId, profile.DisplayName, profile.AvatarUrl, m.Role, m.JoinedAt)
                : new HouseholdMember(m.UserId, "Unknown member", null, m.Role, m.JoinedAt))
            .ToList();
    }

    /// <summary>Creates a single-use invite. Returns null if the user is not an admin of the household.</summary>
    public async Task<CreatedInvite?> CreateInviteAsync(Guid householdId, Guid userId, CancellationToken cancellationToken)
    {
        var isAdmin = await db.Memberships.AnyAsync(
            m => m.HouseholdId == householdId && m.UserId == userId && m.Role == Membership.Admin,
            cancellationToken);
        if (!isAdmin)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var code = InviteCodes.Generate();
        var invite = new Invite
        {
            Id = Guid.CreateVersion7(),
            HouseholdId = householdId,
            CodeHash = InviteCodes.Hash(code),
            CreatedBy = userId,
            CreatedAt = now,
            ExpiresAt = now + InviteLifetime,
        };

        db.Invites.Add(invite);
        await db.SaveChangesAsync(cancellationToken);

        // The only time the plain code exists outside the link. It is never stored.
        return new CreatedInvite(code, invite.ExpiresAt);
    }

    public async Task<AcceptInviteResult> AcceptInviteAsync(string code, Guid userId, CancellationToken cancellationToken)
    {
        var hash = InviteCodes.Hash(code);
        var now = clock.GetUtcNow();

        var invite = await db.Invites.AsNoTracking().SingleOrDefaultAsync(i => i.CodeHash == hash, cancellationToken);

        // One answer for unknown, used and expired codes, so a guesser learns nothing.
        if (invite is null || invite.UsedAt is not null || invite.ExpiresAt <= now)
        {
            return new AcceptInviteResult(InviteOutcome.Invalid, null);
        }

        // Clicking your own household's invite shouldn't burn it.
        if (await db.Memberships.AnyAsync(m => m.HouseholdId == invite.HouseholdId && m.UserId == userId, cancellationToken))
        {
            return new AcceptInviteResult(InviteOutcome.AlreadyMember, invite.HouseholdId);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Claim the invite atomically. If two people open the same link at once, both pass the
        // check above, but only one UPDATE can match "used_at IS NULL"; the other changes 0 rows.
        var claimed = await db.Invites
            .Where(i => i.Id == invite.Id && i.UsedAt == null && i.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(i => i.UsedAt, (DateTimeOffset?)now)
                    .SetProperty(i => i.UsedBy, (Guid?)userId),
                cancellationToken);

        if (claimed == 0)
        {
            return new AcceptInviteResult(InviteOutcome.Invalid, null);
        }

        db.Memberships.Add(new Membership
        {
            HouseholdId = invite.HouseholdId,
            UserId = userId,
            Role = Membership.Member,
            JoinedAt = now,
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AcceptInviteResult(InviteOutcome.Joined, invite.HouseholdId);
    }
}

/// <summary>Households' answer to "which households is this user in?" (ADR-025).</summary>
internal sealed class HouseholdMembership(HouseholdsDbContext db) : IHouseholdMembership
{
    public async Task<IReadOnlyList<Guid>> GetHouseholdIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Memberships
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.JoinedAt)
            .Select(m => m.HouseholdId)
            .ToListAsync(cancellationToken);
}
