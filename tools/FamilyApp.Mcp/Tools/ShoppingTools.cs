using System.ComponentModel;
using ModelContextProtocol.Server;

namespace FamilyApp.Mcp.Tools;

/// <summary>
/// Shopping list tools. The example to copy: one method per tool, a name and description written for
/// the agent, records shaped like the API's JSON, and FamilyApi for the call.
/// </summary>
[McpServerToolType]
public sealed class ShoppingTools(FamilyApi api)
{
    [McpServerTool(Name = "list_shopping", ReadOnly = true, UseStructuredContent = true)]
    [Description("The household's shopping list: everything not yet ticked off, oldest first. " +
                 "Check it before adding items, so you don't add something that's already there.")]
    public Task<ShoppingItem[]> ListShopping(CancellationToken cancellationToken) =>
        api.GetAsync<ShoppingItem[]>("/api/shopping", cancellationToken);

    // TODO(owner): add_shopping_item (POST /api/shopping), tick_shopping_item (POST /api/shopping/{id}/check),
    // mark_shopping_not_needed (POST /api/shopping/{id}/not-needed). No delete (ADR-040).
}

/// <summary>A shopping item as the API returns it. Ids are what the other tools take.</summary>
public sealed record ShoppingItem(
    Guid Id,
    string Name,
    string? Quantity,
    string? Note,
    Guid AddedBy,
    DateTimeOffset AddedAt);
