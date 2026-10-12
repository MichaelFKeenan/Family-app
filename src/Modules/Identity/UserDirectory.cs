using FamilyApp.Modules.Identity.Data;
using FamilyApp.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace FamilyApp.Modules.Identity;

/// <summary>Identity's answer to "who are these users?" for other modules (ADR-025).</summary>
internal sealed class UserDirectory(IdentityModuleDbContext db) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, UserSummary>> GetUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, UserSummary>();
        }

        return await db.Users
            .AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(
                user => user.Id,
                user => new UserSummary(user.Id, user.DisplayName, user.AvatarUrl),
                cancellationToken);
    }
}
