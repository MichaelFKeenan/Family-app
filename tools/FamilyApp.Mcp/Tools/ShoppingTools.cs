using System.ComponentModel;
using ModelContextProtocol.Server;

namespace FamilyApp.Mcp.Tools;

/// <summary>
/// Shopping list tools: one method per tool, a name and description written for the agent,
/// records shaped like the API's JSON, and FamilyApi for the call. No delete (ADR-040).
/// </summary>
[McpServerToolType]
public sealed class ShoppingTools(FamilyApi api)
{
    [McpServerTool(Name = "list_shopping", ReadOnly = true, UseStructuredContent = true)]
    [Description("The household's shopping list: everything not yet ticked off, oldest first. " +
                 "Check it before adding items, so you don't add something that's already there.")]
    public Task<ShoppingItem[]> ListShopping(CancellationToken cancellationToken) =>
        api.GetAsync<ShoppingItem[]>("/api/shopping", cancellationToken);

    [McpServerTool(Name = "add_shopping_item", UseStructuredContent = true)]
    [Description("Adds an item to the household's shopping list. Names are matched case-insensitively: if it's " +
                 "already on the list, nothing changes and alreadyOnList is true, so report it rather than retrying.")]
    public Task<AddShoppingItemResult> AddShoppingItem(
        [Description("What to buy, as you'd say it in a shop: \"Milk\", \"Washing-up liquid\". Up to 100 characters.")] string name,
        [Description("How much, if the user said: \"2\", \"6 pack\", \"1 big bag\". Up to 50 characters.")] string? quantity = null,
        [Description("A detail for whoever shops: brand, size, \"the lemon one\". Up to 200 characters.")] string? note = null,
        CancellationToken cancellationToken = default) =>
        api.PostAsync<AddShoppingItemResult>("/api/shopping", new { name, quantity, note }, cancellationToken);

    [McpServerTool(Name = "tick_shopping_item", Idempotent = true, UseStructuredContent = true)]
    [Description("Ticks an item off the shopping list because it was bought. Counts as a purchase in the history.")]
    public Task<ShoppingItem> TickShoppingItem(
        [Description("The item's id, from list_shopping.")] Guid id,
        CancellationToken cancellationToken = default) =>
        api.PostAsync<ShoppingItem>($"/api/shopping/{id}/check", null, cancellationToken);

    [McpServerTool(Name = "mark_shopping_not_needed", Idempotent = true, UseStructuredContent = true)]
    [Description("Takes an item off the shopping list without buying it (no longer needed, already had some). " +
                 "Kept in history but not counted as a purchase.")]
    public Task<ShoppingItem> MarkShoppingNotNeeded(
        [Description("The item's id, from list_shopping.")] Guid id,
        CancellationToken cancellationToken = default) =>
        api.PostAsync<ShoppingItem>($"/api/shopping/{id}/not-needed", null, cancellationToken);
}

/// <summary>A shopping item as the API returns it. Ids are what the other tools take.</summary>
public sealed record ShoppingItem(
    Guid Id,
    string Name,
    string? Quantity,
    string? Note,
    Guid AddedBy,
    DateTimeOffset AddedAt);

public sealed record AddShoppingItemResult(ShoppingItem Item, bool AlreadyOnList);
