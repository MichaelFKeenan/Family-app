using FamilyApp.Modules.Shopping.Data;
using FamilyApp.SharedKernel;
using FamilyApp.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace FamilyApp.Modules.Shopping;

/// <summary>
/// Shopping list use cases (ADR-010, ADR-035). Unlike tasks there is no version column: an item has
/// one field that changes (checked or not), so each command is a conditional UPDATE whose WHERE clause
/// is the rule ("only if not checked yet"). Postgres decides who wins a race; the loser changes 0 rows.
/// </summary>
public sealed class ShoppingService(ShoppingDbContext db, CurrentHousehold household, DomainEvents events, TimeProvider clock)
{
    public const int MaxName = 100;
    public const int MaxQuantity = 50;
    public const int MaxNote = 200;
    public const int MaxChecked = 100;

    public async Task<IReadOnlyList<ShoppingItemDto>> ActiveAsync(CancellationToken cancellationToken)
    {
        // No household WHERE clause: the global query filter adds it.
        var items = await db.Items.AsNoTracking()
            .Where(i => i.CheckedAt == null)
            .OrderBy(i => i.AddedAt).ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);
        return items.Select(ShoppingItemDto.From).ToList();
    }

    public async Task<IReadOnlyList<ShoppingItemDto>> RecentlyCheckedAsync(int limit, CancellationToken cancellationToken)
    {
        var items = await db.Items.AsNoTracking()
            .Where(i => i.CheckedAt != null)
            .OrderByDescending(i => i.CheckedAt)
            .Take(Math.Clamp(limit, 1, MaxChecked))
            .ToListAsync(cancellationToken);
        return items.Select(ShoppingItemDto.From).ToList();
    }

    public async Task<Outcome<AddItemResult>> AddAsync(AddItemRequest request, Guid userId, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxName)
            return new Outcome<AddItemResult>.Invalid("name", $"Give the item a name of 1 to {MaxName} characters.");
        var quantity = Clean(request.Quantity);
        if (quantity?.Length > MaxQuantity)
            return new Outcome<AddItemResult>.Invalid("quantity", $"Keep the quantity under {MaxQuantity} characters.");
        var note = Clean(request.Note);
        if (note?.Length > MaxNote)
            return new Outcome<AddItemResult>.Invalid("note", $"Keep the note under {MaxNote} characters.");

        var item = new ShoppingItem
        {
            Id = Guid.CreateVersion7(),
            HouseholdId = household.Id,
            Name = name,
            Quantity = quantity,
            Note = note,
            AddedBy = userId,
            AddedAt = clock.GetUtcNow(),
        };

        // Two people adding "Milk" at the same moment must not produce two rows. Checking first and then
        // inserting has a gap between the two steps; ON CONFLICT closes it, because the partial unique index
        // decides. A plain INSERT that hit the index would throw, and in Postgres an error aborts the whole
        // request transaction, so the insert says "do nothing" instead of failing.
        // Typed parameters for the optional columns: a bare null has no type for Postgres to infer from.
        var quantityParameter = new NpgsqlParameter("quantity", NpgsqlDbType.Text) { Value = (object?)item.Quantity ?? DBNull.Value };
        var noteParameter = new NpgsqlParameter("note", NpgsqlDbType.Text) { Value = (object?)item.Note ?? DBNull.Value };

        db.JoinRequestTransaction();
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO shopping.items (id, household_id, name, quantity, note, added_by, added_at)
            VALUES ({item.Id}, {item.HouseholdId}, {item.Name}, {quantityParameter}, {noteParameter}, {item.AddedBy}, {item.AddedAt})
            ON CONFLICT (household_id, lower(name)) WHERE checked_at IS NULL DO NOTHING
            """,
            cancellationToken);

        if (inserted == 1)
        {
            events.Raise(new ShoppingItemAdded(item.HouseholdId, userId, item.Id, item.Name, item.Quantity));
            return new Outcome<AddItemResult>.Ok(new AddItemResult(ShoppingItemDto.From(item), AlreadyOnList: false));
        }

        // Already on the list. Compare with Postgres's lower(), the same function the index uses, so
        // "already on the list" means exactly what the database enforces.
        var existing = await db.Items
            .FromSql($"SELECT * FROM shopping.items WHERE checked_at IS NULL AND lower(name) = lower({name})")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        // Only possible if someone ticked it off between our insert and this read.
        if (existing is null) return new Outcome<AddItemResult>.Conflict($"\"{name}\" just changed. Try again.");

        return new Outcome<AddItemResult>.Ok(new AddItemResult(ShoppingItemDto.From(existing), AlreadyOnList: true));
    }

    /// <summary>
    /// Tick an item off. Idempotent: ticking an already-ticked item succeeds and changes nothing, because
    /// two people in the shop ticking the same thing both meant the same outcome.
    /// </summary>
    public async Task<Outcome<ShoppingItemDto>> CheckAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        db.JoinRequestTransaction();
        var changed = await db.Items
            .Where(i => i.Id == id && i.CheckedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(i => i.CheckedAt, (DateTimeOffset?)now)
                    .SetProperty(i => i.CheckedBy, (Guid?)userId),
                cancellationToken);

        var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null) return new Outcome<ShoppingItemDto>.NotFound();

        // Only the request that actually changed the row records it, so a double tick is one event.
        if (changed == 1) events.Raise(new ShoppingItemChecked(item.HouseholdId, userId, item.Id, item.Name));
        return new Outcome<ShoppingItemDto>.Ok(ShoppingItemDto.From(item));
    }

    /// <summary>Undo a tick. Idempotent like <see cref="CheckAsync"/>.</summary>
    public async Task<Outcome<ShoppingItemDto>> UncheckAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        int changed;
        db.JoinRequestTransaction();
        try
        {
            changed = await db.Items
                .Where(i => i.Id == id && i.CheckedAt != null)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(i => i.CheckedAt, (DateTimeOffset?)null)
                        .SetProperty(i => i.CheckedBy, (Guid?)null),
                    cancellationToken);
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            // The same name was added again since this one was ticked. The failed UPDATE has aborted
            // the transaction, which is fine: a 409 makes the request roll back anyway.
            return new Outcome<ShoppingItemDto>.Conflict("That item is already back on the list.");
        }

        var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null) return new Outcome<ShoppingItemDto>.NotFound();

        if (changed == 1) events.Raise(new ShoppingItemUnchecked(item.HouseholdId, userId, item.Id, item.Name));
        return new Outcome<ShoppingItemDto>.Ok(ShoppingItemDto.From(item));
    }

    /// <summary>
    /// Take an active item off the list without buying it, for mistakes. Ticked items are history and stay,
    /// the same rule as the points ledger: history isn't edited (ADR-035).
    /// </summary>
    public async Task<Outcome<bool>> RemoveAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null) return new Outcome<bool>.NotFound();
        if (item.CheckedAt is not null)
            return new Outcome<bool>.Conflict("Ticked items are kept as history. Untick it first to remove it.");

        db.JoinRequestTransaction();
        var deleted = await db.Items
            .Where(i => i.Id == id && i.CheckedAt == null)
            .ExecuteDeleteAsync(cancellationToken);

        // Someone ticked or removed it between the read and the delete.
        if (deleted == 0) return new Outcome<bool>.Conflict("Someone else just changed this item.");

        events.Raise(new ShoppingItemRemoved(item.HouseholdId, userId, item.Id, item.Name));
        return new Outcome<bool>.Ok(true);
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static bool IsUniqueViolation(Exception ex) =>
        (ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.UniqueViolation;
}
