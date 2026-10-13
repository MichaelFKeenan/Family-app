using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyApp.Modules.Tasks;
using FamilyApp.Modules.Tasks.Data;

namespace FamilyApp.Integration.Tests;

public class TaskTests(FamilyAppFactory factory) : IClassFixture<FamilyAppFactory>
{
    // Must match the app's JSON settings: enums as lower-case strings.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public async Task A_new_task_starts_as_todo_with_sensible_defaults()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Defaults");

        var task = await CreateAsync(admin.Client, new { title = "  Take the bins out  " });

        Assert.Equal("Take the bins out", task.Title);
        Assert.Equal(TaskState.Todo, task.Status);
        Assert.Equal(TaskPriority.Medium, task.Priority);
        Assert.Equal(0, task.Points);
        Assert.Equal(1, task.Version);
        Assert.Equal(admin.Id, task.CreatedBy);
    }

    [Fact]
    public async Task Enums_are_lower_case_strings_on_the_wire()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Wire");
        await CreateAsync(admin.Client, new { title = "Hoover", priority = "high" });

        var raw = await admin.Client.GetStringAsync("/api/tasks?status=todo");

        Assert.Contains("\"status\":\"todo\"", raw);
        Assert.Contains("\"priority\":\"high\"", raw);
    }

    [Fact]
    public async Task A_task_moves_through_the_board()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Lifecycle", members: 1);
        var task = await CreateAsync(admin.Client, new { title = "Wash the car", points = 10 });

        var started = await PostAsync(members[0].Client, $"/api/tasks/{task.Id}/start");
        var completed = await PostAsync(members[0].Client, $"/api/tasks/{task.Id}/complete");
        var reopened = await PostAsync(admin.Client, $"/api/tasks/{task.Id}/reopen");

        Assert.Equal(TaskState.Doing, started.Status);
        Assert.Equal(TaskState.Done, completed.Status);
        Assert.Equal(members[0].Id, completed.CompletedBy);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal(TaskState.Todo, reopened.Status);
        Assert.Null(reopened.CompletedAt);
        Assert.Equal(4, reopened.Version);
    }

    [Fact]
    public async Task Impossible_moves_are_refused()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Rules");
        var task = await CreateAsync(admin.Client, new { title = "Mow the lawn" });
        await PostAsync(admin.Client, $"/api/tasks/{task.Id}/complete");

        var completeAgain = await admin.Client.PostAsync($"/api/tasks/{task.Id}/complete", null);
        var startDone = await admin.Client.PostAsync($"/api/tasks/{task.Id}/start", null);

        Assert.Equal(HttpStatusCode.Conflict, completeAgain.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, startDone.StatusCode);
    }

    [Fact]
    public async Task Two_people_completing_at_once_cannot_both_succeed()
    {
        // From step 6 completion awards points; a double completion would pay out twice.
        var (_, admin, members) = await factory.NewHouseholdAsync("Race", members: 3);
        var task = await CreateAsync(admin.Client, new { title = "Feed the cat", points = 5 });
        var everyone = members.Append(admin).ToArray();

        var responses = await Task.WhenAll(everyone.Select(u => u.Client.PostAsync($"/api/tasks/{task.Id}/complete", null)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task An_edit_based_on_an_old_version_is_rejected()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Stale", members: 1);
        var task = await CreateAsync(admin.Client, new { title = "Paint the fence" });

        // Both open the edit form at version 1. The first save wins and bumps the version.
        var first = await admin.Client.PutAsJsonAsync($"/api/tasks/{task.Id}", new { title = "Paint the fence white", version = 1 }, Json);
        var second = await members[0].Client.PutAsJsonAsync($"/api/tasks/{task.Id}", new { title = "Paint the fence blue", version = 1 }, Json);
        var final = await GetAsync(admin.Client, task.Id);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Paint the fence white", final.Title);
        Assert.Equal(2, final.Version);
    }

    [Fact]
    public async Task Tasks_are_invisible_to_other_households()
    {
        var (_, smith, _) = await factory.NewHouseholdAsync("Smiths");
        var (_, jones, _) = await factory.NewHouseholdAsync("Joneses");
        var secret = await CreateAsync(smith.Client, new { title = "Smiths' secret task" });

        var list = await jones.Client.GetFromJsonAsync<List<TaskDto>>("/api/tasks", Json);
        var get = await jones.Client.GetAsync($"/api/tasks/{secret.Id}");
        var complete = await jones.Client.PostAsync($"/api/tasks/{secret.Id}/complete", null);
        var delete = await jones.Client.DeleteAsync($"/api/tasks/{secret.Id}");
        var stillThere = await GetAsync(smith.Client, secret.Id);

        Assert.DoesNotContain(list!, t => t.Id == secret.Id);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, complete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(TaskState.Todo, stillThere.Status);
    }

    [Fact]
    public async Task Tasks_can_only_be_assigned_to_household_members()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Assign", members: 1);
        var outsider = await factory.NewUserAsync("Outsider");
        var task = await CreateAsync(admin.Client, new { title = "Walk the dog" });

        var toMember = await admin.Client.PostAsJsonAsync($"/api/tasks/{task.Id}/assign", new { assigneeId = members[0].Id });
        var toOutsider = await admin.Client.PostAsJsonAsync($"/api/tasks/{task.Id}/assign", new { assigneeId = outsider.Id });
        var onCreate = await admin.Client.PostAsJsonAsync("/api/tasks", new { title = "Sneaky", assigneeId = outsider.Id });

        Assert.Equal(HttpStatusCode.OK, toMember.StatusCode);
        Assert.Equal(members[0].Id, (await toMember.Content.ReadFromJsonAsync<TaskDto>(Json))!.AssigneeId);
        Assert.Equal(HttpStatusCode.BadRequest, toOutsider.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, onCreate.StatusCode);
    }

    [Fact]
    public async Task Filters_narrow_the_board()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Filters", members: 1);
        var categories = await admin.Client.GetFromJsonAsync<List<CategoryDto>>("/api/tasks/categories", Json);
        var house = categories!.Single(c => c.Name == "House");
        await CreateAsync(admin.Client, new { title = "Clean windows", categoryId = house.Id, assigneeId = members[0].Id });
        await CreateAsync(admin.Client, new { title = "Book holiday" });

        var byCategory = await admin.Client.GetFromJsonAsync<List<TaskDto>>($"/api/tasks?categoryId={house.Id}", Json);
        var byAssignee = await admin.Client.GetFromJsonAsync<List<TaskDto>>($"/api/tasks?assigneeId={members[0].Id}", Json);

        Assert.Equal("Clean windows", Assert.Single(byCategory!).Title);
        Assert.Equal("Clean windows", Assert.Single(byAssignee!).Title);
    }

    [Fact]
    public async Task Invalid_input_is_rejected()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Validation");

        var noTitle = await admin.Client.PostAsJsonAsync("/api/tasks", new { title = " " });
        var tooManyPoints = await admin.Client.PostAsJsonAsync("/api/tasks", new { title = "Greedy", points = 5000 });
        var unknownCategory = await admin.Client.PostAsJsonAsync("/api/tasks", new { title = "Lost", categoryId = Guid.CreateVersion7() });

        Assert.Equal(HttpStatusCode.BadRequest, noTitle.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooManyPoints.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownCategory.StatusCode);
    }

    [Fact]
    public async Task New_households_get_default_categories_once()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Categories");

        // Simultaneous first visits must not create duplicates.
        var lists = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => admin.Client.GetFromJsonAsync<List<CategoryDto>>("/api/tasks/categories", Json)));

        Assert.All(lists, list => Assert.Equal(new[] { "Short term", "Long term", "House" }, list!.Select(c => c.Name)));
    }

    [Fact]
    public async Task Another_households_category_cannot_be_used()
    {
        var (_, smith, _) = await factory.NewHouseholdAsync("Category owners");
        var (_, jones, _) = await factory.NewHouseholdAsync("Category thieves");
        var smithCategory = (await smith.Client.GetFromJsonAsync<List<CategoryDto>>("/api/tasks/categories", Json))![0];

        var response = await jones.Client.PostAsJsonAsync("/api/tasks", new { title = "Borrowed", categoryId = smithCategory.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_category_uncategorises_its_tasks()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Recategorise");
        var created = await admin.Client.PostAsJsonAsync("/api/tasks/categories", new { name = "Garden" });
        var garden = (await created.Content.ReadFromJsonAsync<CategoryDto>(Json))!;
        var task = await CreateAsync(admin.Client, new { title = "Weed the beds", categoryId = garden.Id });

        var duplicate = await admin.Client.PostAsJsonAsync("/api/tasks/categories", new { name = "Garden" });
        var deleted = await admin.Client.DeleteAsync($"/api/tasks/categories/{garden.Id}");
        var after = await GetAsync(admin.Client, task.Id);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Null(after.CategoryId);
    }

    [Fact]
    public async Task Deleted_tasks_are_gone()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Delete");
        var task = await CreateAsync(admin.Client, new { title = "Temporary" });

        var deleted = await admin.Client.DeleteAsync($"/api/tasks/{task.Id}");
        var get = await admin.Client.GetAsync($"/api/tasks/{task.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    private static async Task<TaskDto> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/tasks", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskDto>(Json))!;
    }

    private static async Task<TaskDto> PostAsync(HttpClient client, string path)
    {
        var response = await client.PostAsync(path, null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TaskDto>(Json))!;
    }

    private static async Task<TaskDto> GetAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<TaskDto>($"/api/tasks/{id}", Json))!;
}
