using FamilyApp.Modules.Shopping.Data;

namespace FamilyApp.Modules.Shopping;

public sealed record ShoppingItemDto(
    Guid Id,
    string Name,
    string? Quantity,
    string? Note,
    Guid AddedBy,
    DateTimeOffset AddedAt,
    Guid? CheckedBy,
    DateTimeOffset? CheckedAt)
{
    internal static ShoppingItemDto From(ShoppingItem item) => new(
        item.Id, item.Name, item.Quantity, item.Note, item.AddedBy, item.AddedAt, item.CheckedBy, item.CheckedAt);
}

public sealed record AddItemRequest(string? Name, string? Quantity, string? Note);

/// <summary>
/// The result of adding. When the item is already on the list, <see cref="AlreadyOnList"/> is true
/// and <see cref="Item"/> is the existing one, unchanged (ADR-035).
/// </summary>
public sealed record AddItemResult(ShoppingItemDto Item, bool AlreadyOnList);

/// <summary>What a shopping operation produced, mapped to HTTP by the endpoints.</summary>
public abstract record Outcome<T>
{
    public sealed record Ok(T Value) : Outcome<T>;
    public sealed record NotFound : Outcome<T>;
    public sealed record Conflict(string Message) : Outcome<T>;
    public sealed record Invalid(string Field, string Message) : Outcome<T>;
}
