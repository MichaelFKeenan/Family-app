using FamilyApp.SharedKernel;
using Microsoft.AspNetCore.SignalR;

namespace FamilyApp.Host.Live;

/// <summary>
/// The browser's live connection (ADR-042). It only listens: the server sends <c>changed</c> with the
/// topics to refetch. Clients call nothing on it, so there is nothing here to authorise per call.
/// </summary>
public sealed class LiveHub(IHouseholdMembership membership) : Hub
{
    public const string ReadyMethod = "ready";

    public override async Task OnConnectedAsync()
    {
        if (!Context.User!.TryGetUserId(out var userId))
        {
            Context.Abort();
            return;
        }

        // Checked once, on connect. A browser that joins a household reconnects to be added.
        foreach (var householdId in await membership.GetHouseholdIdsAsync(userId, Context.ConnectionAborted))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, LiveUpdates.HouseholdGroup(householdId));
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, LiveUpdates.UserGroup(userId));

        // The client's handshake completes before this method runs, so a change in between would be
        // missed. "ready" says the groups are joined: the browser refetches then, closing that gap,
        // and the same refetch catches up on anything missed while it was disconnected.
        await Clients.Caller.SendAsync(ReadyMethod);
    }
}
