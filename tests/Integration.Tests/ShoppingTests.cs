using System.Net;
using System.Net.Http.Json;
using FamilyApp.Host.Events;
using FamilyApp.Modules.Shopping;
using FamilyApp.Modules.Shopping.Data;

namespace FamilyApp.Integration.Tests;

public class ShoppingTests(FamilyAppFactory factory) : IClassFixture<FamilyAppFactory>
{
    [Fact]
    public async Task Added_items_appear_on_the_active_list_in_order()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Groceries", members: 1);

        var response = await admin.Client.PostAsJsonAsync("/api/shopping", new { name = "  Milk ", quantity = "2", note = "semi-skimmed" });
        await AddAsync(members[0].Client, "Bread");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var list = await ActiveAsync(members[0].Client);
        Assert.Equal(new[] { "Milk", "Bread" }, list.Select(i => i.Name));
        Assert.Equal("2", list[0].Quantity);
        Assert.Equal("semi-skimmed", list[0].Note);
        Assert.Equal(admin.Id, list[0].AddedBy);
        Assert.Null(list[0].CheckedAt);
    }

    [Fact]
    public async Task Adding_something_already_on_the_list_returns_the_existing_item()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Duplicates", members: 1);
        var first = await AddAsync(admin.Client, "Eggs");

        var second = await members[0].Client.PostAsJsonAsync("/api/shopping", new { name = "EGGS" });
        var result = (await second.Content.ReadFromJsonAsync<AddItemResult>(ActivityLog.PayloadJson))!;

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True(result.AlreadyOnList);
        Assert.Equal(first.Id, result.Item.Id);
        Assert.Single(await ActiveAsync(admin.Client));
    }

    [Fact]
    public async Task Simultaneous_adds_of_the_same_item_make_one_row()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Race", members: 2);
        var clients = new[] { admin.Client, members[0].Client, members[1].Client };

        var responses = await Task.WhenAll(clients.Select(c => c.PostAsJsonAsync("/api/shopping", new { name = "Butter" })));

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, $"Got {(int)r.StatusCode}"));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(await ActiveAsync(admin.Client));
    }

    [Fact]
    public async Task Checking_moves_an_item_to_history_and_unchecking_brings_it_back()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Ticks", members: 1);
        var item = await AddAsync(admin.Client, "Apples");

        var checkedItem = await PostAsync(members[0].Client, $"/api/shopping/{item.Id}/check");

        Assert.Equal(members[0].Id, checkedItem.CheckedBy);
        Assert.NotNull(checkedItem.CheckedAt);
        Assert.Empty(await ActiveAsync(admin.Client));
        Assert.Equal(item.Id, Assert.Single(await CheckedAsync(admin.Client)).Id);

        var unchecked_ = await PostAsync(admin.Client, $"/api/shopping/{item.Id}/uncheck");

        Assert.Null(unchecked_.CheckedAt);
        Assert.Null(unchecked_.CheckedBy);
        Assert.Single(await ActiveAsync(admin.Client));
        Assert.Empty(await CheckedAsync(admin.Client));
    }

    [Fact]
    public async Task Not_needed_leaves_the_list_but_is_not_a_purchase()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Changed my mind");
        var bought = await AddAsync(admin.Client, "Pasta");
        var skipped = await AddAsync(admin.Client, "Crisps");

        var boughtResult = await PostAsync(admin.Client, $"/api/shopping/{bought.Id}/check");
        var skippedResult = await PostAsync(admin.Client, $"/api/shopping/{skipped.Id}/not-needed");

        Assert.Equal(CheckReason.Bought, boughtResult.CheckReason);
        Assert.Equal(CheckReason.NotNeeded, skippedResult.CheckReason);
        Assert.Empty(await ActiveAsync(admin.Client));
        var raw = await admin.Client.GetStringAsync("/api/shopping/checked");
        Assert.Contains("\"checkReason\":\"notNeeded\"", raw);

        var reasons = (await ActivityAsync(admin.Client))
            .Where(a => a.Type == "ShoppingItemChecked")
            .ToDictionary(a => a.Payload.GetProperty("name").GetString()!, a => a.Payload.GetProperty("reason").GetString());
        Assert.Equal("bought", reasons["Pasta"]);
        Assert.Equal("notNeeded", reasons["Crisps"]);
    }

    [Fact]
    public async Task The_first_tick_wins_and_unticking_clears_the_reason()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("First wins", members: 1);
        var item = await AddAsync(admin.Client, "Yoghurt");

        await PostAsync(admin.Client, $"/api/shopping/{item.Id}/not-needed");
        var second = await PostAsync(members[0].Client, $"/api/shopping/{item.Id}/check");
        var unticked = await PostAsync(admin.Client, $"/api/shopping/{item.Id}/uncheck");

        Assert.Equal(CheckReason.NotNeeded, second.CheckReason);
        Assert.Equal(admin.Id, second.CheckedBy);
        Assert.Null(unticked.CheckReason);
    }

    [Fact]
    public async Task Ticking_twice_is_harmless_and_recorded_once()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Double tick", members: 1);
        var item = await AddAsync(admin.Client, "Cheese");

        var responses = await Task.WhenAll(
            admin.Client.PostAsync($"/api/shopping/{item.Id}/check", null),
            members[0].Client.PostAsync($"/api/shopping/{item.Id}/check", null));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var activity = await ActivityAsync(admin.Client);
        Assert.Single(activity, a => a.Type == "ShoppingItemChecked");
    }

    [Fact]
    public async Task Bought_again_later_is_a_new_row_so_history_keeps_both()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("History");
        var first = await AddAsync(admin.Client, "Coffee");
        await PostAsync(admin.Client, $"/api/shopping/{first.Id}/check");

        var second = await AddAsync(admin.Client, "Coffee");
        await PostAsync(admin.Client, $"/api/shopping/{second.Id}/check");

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, (await CheckedAsync(admin.Client)).Count(i => i.Name == "Coffee"));
    }

    [Fact]
    public async Task Unticking_is_refused_when_the_same_item_is_already_back_on_the_list()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Back again");
        var old = await AddAsync(admin.Client, "Rice");
        await PostAsync(admin.Client, $"/api/shopping/{old.Id}/check");
        await AddAsync(admin.Client, "rice");

        var response = await admin.Client.PostAsync($"/api/shopping/{old.Id}/uncheck", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(await ActiveAsync(admin.Client));
    }

    [Fact]
    public async Task Active_items_can_be_removed_but_ticked_ones_are_history()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Removal");
        var mistake = await AddAsync(admin.Client, "Mlik");
        var bought = await AddAsync(admin.Client, "Jam");
        await PostAsync(admin.Client, $"/api/shopping/{bought.Id}/check");

        var removeMistake = await admin.Client.DeleteAsync($"/api/shopping/{mistake.Id}");
        var removeBought = await admin.Client.DeleteAsync($"/api/shopping/{bought.Id}");

        Assert.Equal(HttpStatusCode.NoContent, removeMistake.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, removeBought.StatusCode);
        Assert.Empty(await ActiveAsync(admin.Client));
        Assert.Single(await CheckedAsync(admin.Client));
    }

    [Fact]
    public async Task Invalid_items_are_rejected()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Validation");

        var blank = await admin.Client.PostAsJsonAsync("/api/shopping", new { name = "   " });
        var tooLong = await admin.Client.PostAsJsonAsync("/api/shopping", new { name = new string('x', 101) });

        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Another_household_cannot_see_or_touch_the_list()
    {
        var (_, smith, _) = await factory.NewHouseholdAsync("Smiths' list");
        var (_, jones, _) = await factory.NewHouseholdAsync("Joneses' list");
        var item = await AddAsync(smith.Client, "Tea");

        var check = await jones.Client.PostAsync($"/api/shopping/{item.Id}/check", null);
        var remove = await jones.Client.DeleteAsync($"/api/shopping/{item.Id}");
        // Same name in another household is a separate item, not a duplicate.
        var theirTea = await jones.Client.PostAsJsonAsync("/api/shopping", new { name = "Tea" });

        Assert.Equal(HttpStatusCode.NotFound, check.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
        Assert.Equal(HttpStatusCode.Created, theirTea.StatusCode);
        Assert.Equal(item.Id, Assert.Single(await ActiveAsync(smith.Client)).Id);
        Assert.DoesNotContain(await ActiveAsync(jones.Client), i => i.Id == item.Id);
    }

    [Fact]
    public async Task Each_change_is_recorded_in_the_activity_log()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Shopping story");
        var item = await AddAsync(admin.Client, "Oranges", quantity: "6");
        await PostAsync(admin.Client, $"/api/shopping/{item.Id}/check");
        await PostAsync(admin.Client, $"/api/shopping/{item.Id}/uncheck");
        (await admin.Client.DeleteAsync($"/api/shopping/{item.Id}")).EnsureSuccessStatusCode();

        var types = (await ActivityAsync(admin.Client))
            .Where(a => a.Type.StartsWith("Shopping", StringComparison.Ordinal))
            .Reverse()
            .Select(a => a.Type);

        Assert.Equal(new[] { "ShoppingItemAdded", "ShoppingItemChecked", "ShoppingItemUnchecked", "ShoppingItemRemoved" }, types);
    }

    private static async Task<ShoppingItemDto> AddAsync(HttpClient client, string name, string? quantity = null)
    {
        var response = await client.PostAsJsonAsync("/api/shopping", new { name, quantity });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AddItemResult>(ActivityLog.PayloadJson))!.Item;
    }

    private static async Task<ShoppingItemDto> PostAsync(HttpClient client, string path)
    {
        var response = await client.PostAsync(path, null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ShoppingItemDto>(ActivityLog.PayloadJson))!;
    }

    private static async Task<List<ShoppingItemDto>> ActiveAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<ShoppingItemDto>>("/api/shopping", ActivityLog.PayloadJson))!;

    private static async Task<List<ShoppingItemDto>> CheckedAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<ShoppingItemDto>>("/api/shopping/checked", ActivityLog.PayloadJson))!;

    private static async Task<List<ActivityEntry>> ActivityAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<ActivityEntry>>("/api/activity?limit=100", ActivityLog.PayloadJson))!;
}
