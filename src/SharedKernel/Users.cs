namespace FamilyApp.SharedKernel;

/// <summary>What other modules may know about a user: enough to show them, nothing more.</summary>
public sealed record UserSummary(Guid Id, string DisplayName, string? AvatarUrl);

/// <summary>Read-only lookup of users; the Identity module answers it (ADR-025).</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, UserSummary>> GetUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);
}
