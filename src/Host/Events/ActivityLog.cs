using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyApp.SharedKernel.Events;
using FamilyApp.SharedKernel.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace FamilyApp.Host.Events;

/// <summary>
/// Append-only history of every domain event (ADR-005). Not the source of truth (the module
/// tables are); it records who did what and when, for the activity feed and, later, for the
/// LLM features to analyse. Rows are never updated or deleted.
/// </summary>
public sealed class ActivityLog(DbSession session, TimeProvider clock)
{
    public static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public async Task RecordAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        // Same connection and transaction as the change that raised the event.
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO platform.activity_events (id, household_id, actor_id, type, payload, occurred_at)
            VALUES ($1, $2, $3, $4, $5, $6)
            """,
            session.Connection,
            session.Transaction);

        command.Parameters.Add(new NpgsqlParameter { Value = Guid.CreateVersion7() });
        command.Parameters.Add(new NpgsqlParameter { Value = domainEvent.HouseholdId });
        command.Parameters.Add(new NpgsqlParameter { Value = domainEvent.ActorId });
        command.Parameters.Add(new NpgsqlParameter { Value = domainEvent.GetType().Name });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Jsonb,
            Value = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), PayloadJson),
        });
        command.Parameters.Add(new NpgsqlParameter { Value = clock.GetUtcNow() });

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
