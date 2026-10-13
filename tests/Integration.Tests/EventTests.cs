using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FamilyApp.Host.Events;
using FamilyApp.Modules.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyApp.Integration.Tests;

public class EventTests(FamilyAppFactory factory) : IClassFixture<FamilyAppFactory>
{
    [Fact]
    public async Task Creating_a_household_is_logged_and_seeds_categories_through_an_event()
    {
        var (household, admin, _) = await factory.NewHouseholdAsync("Evented");

        var activity = await ActivityAsync(admin.Client);
        var categories = await admin.Client.GetFromJsonAsync<List<CategoryDto>>("/api/tasks/categories");

        var created = Assert.Single(activity, a => a.Type == "HouseholdCreated");
        Assert.Equal(admin.Id, created.ActorId);
        Assert.Equal("Evented", created.Payload.GetProperty("name").GetString());
        Assert.Equal(household.Id, created.Payload.GetProperty("householdId").GetGuid());
        Assert.Equal(new[] { "Short term", "Long term", "House" }, categories!.Select(c => c.Name));
    }

    [Fact]
    public async Task Deleting_every_category_no_longer_brings_the_defaults_back()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Tidy");
        var categories = await admin.Client.GetFromJsonAsync<List<CategoryDto>>("/api/tasks/categories");

        foreach (var category in categories!)
        {
            (await admin.Client.DeleteAsync($"/api/tasks/categories/{category.Id}")).EnsureSuccessStatusCode();
        }

        Assert.Empty((await admin.Client.GetFromJsonAsync<List<CategoryDto>>("/api/tasks/categories"))!);
    }

    [Fact]
    public async Task Each_task_change_is_recorded_in_order()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Story", members: 1);
        var create = await admin.Client.PostAsJsonAsync("/api/tasks", new { title = "Clean the oven", points = 20 });
        var task = (await create.Content.ReadFromJsonAsync<TaskDto>(ActivityLog.PayloadJson))!;

        await admin.Client.PostAsJsonAsync($"/api/tasks/{task.Id}/assign", new { assigneeId = members[0].Id });
        await members[0].Client.PostAsync($"/api/tasks/{task.Id}/start", null);
        await members[0].Client.PostAsync($"/api/tasks/{task.Id}/complete", null);

        var activity = await ActivityAsync(admin.Client);
        var forTask = activity
            .Where(a => a.Type.StartsWith("Task", StringComparison.Ordinal))
            .Where(a => a.Payload.TryGetProperty("taskId", out var id) && id.GetGuid() == task.Id)
            .Reverse()
            .ToList();

        Assert.Equal(new[] { "TaskCreated", "TaskAssigned", "TaskStarted", "TaskCompleted" }, forTask.Select(a => a.Type));
        var completed = forTask[^1];
        Assert.Equal(members[0].Id, completed.ActorId);
        Assert.Equal(20, completed.Payload.GetProperty("points").GetInt32());
        Assert.Equal(members[0].Id, completed.Payload.GetProperty("assigneeId").GetGuid());
    }

    [Fact]
    public async Task Failed_requests_record_nothing()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Quiet");
        var before = (await ActivityAsync(admin.Client)).Count;

        var invalid = await admin.Client.PostAsJsonAsync("/api/tasks", new { title = "" });
        var missing = await admin.Client.PostAsync($"/api/tasks/{Guid.CreateVersion7()}/complete", null);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(before, (await ActivityAsync(admin.Client)).Count);
    }

    [Fact]
    public async Task A_failing_handler_rolls_back_the_change_that_raised_the_event()
    {
        var (household, admin, _) = await factory.NewHouseholdAsync("Atomic");

        try
        {
            var response = await admin.Client.PostAsJsonAsync("/api/tasks", new { title = FailingHandler.Trigger });
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        catch (InvalidOperationException)
        {
            // The test server may surface the server-side exception directly instead of a 500.
        }

        // Neither the task nor its activity row survived: they were in the same transaction.
        await using var command = factory.Services.GetRequiredService<NpgsqlDataSource>().CreateCommand(
            """
            SELECT (SELECT count(*) FROM tasks.tasks WHERE household_id = $1 AND title = $2)
                 + (SELECT count(*) FROM platform.activity_events WHERE household_id = $1 AND type = 'TaskCreated')
            """);
        command.Parameters.Add(new NpgsqlParameter { Value = household.Id });
        command.Parameters.Add(new NpgsqlParameter { Value = FailingHandler.Trigger });

        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Joining_is_recorded_as_the_person_who_joined()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Joiners", members: 1);

        var joined = Assert.Single(await ActivityAsync(admin.Client), a => a.Type == "MemberJoined");

        Assert.Equal(members[0].Id, joined.ActorId);
    }

    [Fact]
    public async Task Activity_is_private_to_the_household()
    {
        var (_, smith, _) = await factory.NewHouseholdAsync("Private Smiths");
        var (_, jones, _) = await factory.NewHouseholdAsync("Private Joneses");
        await smith.Client.PostAsJsonAsync("/api/tasks", new { title = "Smiths only" });

        var jonesActivity = await ActivityAsync(jones.Client);

        Assert.DoesNotContain(jonesActivity, a => a.Payload.TryGetProperty("title", out var t) && t.GetString() == "Smiths only");
        Assert.All(jonesActivity, a => Assert.Equal("Private Joneses", NameOrNull(a) ?? "Private Joneses"));
    }

    private static string? NameOrNull(ActivityEntry entry) =>
        entry.Type == "HouseholdCreated" ? entry.Payload.GetProperty("name").GetString() : null;

    private static async Task<List<ActivityEntry>> ActivityAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<ActivityEntry>>("/api/activity?limit=100", ActivityLog.PayloadJson))!;
}
