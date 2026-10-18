using FamilyApp.SharedKernel.Events;
using Microsoft.AspNetCore.SignalR;

namespace FamilyApp.Host.Live;

/// <summary>
/// Tells other screens what changed, after the request has committed (ADR-042). The message is only
/// a list of topics, which are the browser's query keys; the browser refetches them through the API,
/// so nothing here needs to know who may see what beyond which group to tell.
/// </summary>
public sealed class LiveUpdates(IHubContext<LiveHub> hub, ILogger<LiveUpdates> logger)
{
    public const string ChangedMethod = "changed";

    public static string HouseholdGroup(Guid householdId) => $"household:{householdId}";

    public static string UserGroup(Guid userId) => $"user:{userId}";

    public async Task PublishAsync(IReadOnlyList<IDomainEvent> events)
    {
        // One message per group per request, however many events it raised.
        var topicsByGroup = new Dictionary<string, SortedSet<string>>();
        foreach (var domainEvent in events)
        {
            // Personal-board events go only to their owner (ADR-037).
            var group = domainEvent is IBoardEvent { OwnerId: { } ownerId }
                ? UserGroup(ownerId)
                : HouseholdGroup(domainEvent.HouseholdId);

            if (!topicsByGroup.TryGetValue(group, out var topics))
            {
                topicsByGroup[group] = topics = [];
            }
            topics.UnionWith(TopicsFor(domainEvent));
        }

        foreach (var (group, topics) in topicsByGroup)
        {
            try
            {
                await hub.Clients.Group(group).SendAsync(ChangedMethod, topics.ToArray());
            }
            catch (Exception ex)
            {
                // The change is committed; a lost nudge is caught up by the next focus or reconnect.
                logger.LogWarning(ex, "Couldn't send live update to {Group}", group);
            }
        }
    }

    /// <summary>Which of the browser's queries an event makes stale. Every event appears in the activity feed.</summary>
    internal static IEnumerable<string> TopicsFor(IDomainEvent domainEvent) => domainEvent switch
    {
        ShoppingItemAdded or ShoppingItemChecked or ShoppingItemUnchecked or ShoppingItemRemoved => ["shopping", "activity"],
        IBoardEvent or TaskMadePersonal or TaskSharedWithFamily => ["tasks", "activity"],
        PointsAwarded or PointsReversed => ["leaderboard", "activity"],
        MemberJoined => ["members", "households", "activity"],
        HouseholdCreated => ["households", "activity"],
        _ => ["activity"],
    };
}
