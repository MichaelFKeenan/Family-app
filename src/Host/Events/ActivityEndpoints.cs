using System.Text.Json;
using FamilyApp.SharedKernel;
using Npgsql;

namespace FamilyApp.Host.Events;

public sealed record ActivityEntry(Guid Id, string Type, Guid ActorId, JsonElement Payload, DateTimeOffset OccurredAt);

public static class ActivityEndpoints
{
    public const int MaxLimit = 100;

    /// <summary>GET /api/activity?limit=20: the current household's most recent events, newest first.</summary>
    public static void MapActivity(this IEndpointRouteBuilder api)
    {
        api.MapGet("/activity", async (int? limit, CurrentHousehold household, CurrentUser user, NpgsqlDataSource dataSource, CancellationToken ct) =>
        {
            await using var command = dataSource.CreateCommand(
                """
                SELECT id, type, actor_id, payload::text, occurred_at
                FROM platform.activity_events
                WHERE household_id = $1
                  -- Personal-task events are private to their owner (ADR-037). Older events have no
                  -- ownerId, which reads as NULL: family events, visible to everyone.
                  AND (payload->>'ownerId' IS NULL OR payload->>'ownerId' = $3)
                ORDER BY occurred_at DESC, id DESC
                LIMIT $2
                """);
            command.Parameters.Add(new NpgsqlParameter { Value = household.Id });
            command.Parameters.Add(new NpgsqlParameter { Value = Math.Clamp(limit ?? 20, 1, MaxLimit) });
            // Guids serialise to JSON in lower-case "D" format, which is what ToString() gives.
            command.Parameters.Add(new NpgsqlParameter { Value = user.Id.ToString() });

            var entries = new List<ActivityEntry>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                using var payload = JsonDocument.Parse(reader.GetString(3));
                entries.Add(new ActivityEntry(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetGuid(2),
                    payload.RootElement.Clone(),
                    reader.GetFieldValue<DateTimeOffset>(4)));
            }

            return entries;
        }).RequireHousehold();
    }
}
