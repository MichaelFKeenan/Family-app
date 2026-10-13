using FamilyApp.Modules.Tasks.Data;
using FamilyApp.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;

namespace FamilyApp.Modules.Tasks;

/// <summary>
/// Gives every new household the default categories, in the same transaction that creates the
/// household (ADR-031). Replaces the create-on-first-read approach of ADR-029. The Households
/// module doesn't know this happens; it only announced that a household was created.
/// </summary>
internal sealed class SeedDefaultCategories(TasksDbContext db, TimeProvider clock) : DomainEventHandler<HouseholdCreated>
{
    internal static readonly string[] Defaults = ["Short term", "Long term", "House"];

    public override async Task HandleAsync(HouseholdCreated created, CancellationToken cancellationToken)
    {
        // Raw SQL with the household id from the event: this request has no CurrentHousehold
        // (creating a household isn't a household-scoped request), so the query filter can't be used.
        db.JoinRequestTransaction();
        var now = clock.GetUtcNow();
        for (var i = 0; i < Defaults.Length; i++)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO tasks.categories (id, household_id, name, sort_order, created_at)
                VALUES ({Guid.CreateVersion7()}, {created.HouseholdId}, {Defaults[i]}, {i}, {now})
                ON CONFLICT (household_id, name) DO NOTHING
                """,
                cancellationToken);
        }
    }
}
