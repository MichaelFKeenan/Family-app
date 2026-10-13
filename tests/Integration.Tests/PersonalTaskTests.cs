using System.Net;
using System.Net.Http.Json;
using FamilyApp.Host.Events;
using FamilyApp.Modules.Tasks;

namespace FamilyApp.Integration.Tests;

/// <summary>Personal boards (ADR-037): private to their owner, no points, tasks can move both ways.</summary>
public class PersonalTaskTests(FamilyAppFactory factory) : IClassFixture<FamilyAppFactory>
{
    private sealed record LeaderboardRow(Guid UserId, int Points);

    [Fact]
    public async Task A_personal_task_is_on_its_owners_board_and_nowhere_else()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Private", members: 1);
        var mine = await CreateAsync(admin.Client, new { title = "Renew my passport", personal = true });
        var family = await CreateAsync(admin.Client, new { title = "Clean the windows" });

        Assert.Equal(admin.Id, mine.OwnerId);
        Assert.Null(family.OwnerId);
        Assert.Equal(new[] { mine.Id }, (await BoardAsync(admin.Client, "personal")).Select(t => t.Id));
        Assert.Equal(new[] { family.Id }, (await BoardAsync(admin.Client, "family")).Select(t => t.Id));
        // No board parameter means the family board, as before.
        Assert.Equal(new[] { family.Id }, (await BoardAsync(admin.Client, null)).Select(t => t.Id));
        Assert.Empty(await BoardAsync(members[0].Client, "personal"));
        Assert.DoesNotContain(await BoardAsync(members[0].Client, "family"), t => t.Id == mine.Id);
    }

    [Fact]
    public async Task Another_member_cannot_read_or_change_a_personal_task()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Hands off", members: 1);
        var mine = await CreateAsync(admin.Client, new { title = "Birthday present for Sam", personal = true });
        var other = members[0].Client;

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/tasks/{mine.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/tasks/{mine.Id}/complete", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/tasks/{mine.Id}/move-to-family", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/tasks/{mine.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.PutAsJsonAsync($"/api/tasks/{mine.Id}", new { title = "Peek", version = mine.Version })).StatusCode);
    }

    [Fact]
    public async Task Personal_activity_is_shown_only_to_its_owner()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Quiet", members: 1);
        var mine = await CreateAsync(admin.Client, new { title = "Book dentist", personal = true });
        await PostAsync(admin.Client, $"/api/tasks/{mine.Id}/complete");

        var ownerSees = await ActivityAsync(admin.Client);
        var otherSees = await ActivityAsync(members[0].Client);

        Assert.Contains(ownerSees, a => a.Type == "TaskCompleted" && a.Payload.GetProperty("taskId").GetGuid() == mine.Id);
        Assert.DoesNotContain(otherSees, a => a.Payload.TryGetProperty("taskId", out var id) && id.GetGuid() == mine.Id);
    }

    [Fact]
    public async Task Personal_tasks_earn_no_points_until_moved_to_the_family_board()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("No farming");
        var first = await CreateAsync(admin.Client, new { title = "Go for a run", points = 30, personal = true });
        await PostAsync(admin.Client, $"/api/tasks/{first.Id}/complete");

        Assert.Equal(0, await PointsAsync(admin));

        var second = await CreateAsync(admin.Client, new { title = "Paint the fence", points = 20, personal = true });
        var moved = await PostAsync(admin.Client, $"/api/tasks/{second.Id}/move-to-family");
        await PostAsync(admin.Client, $"/api/tasks/{second.Id}/complete");

        Assert.Null(moved.OwnerId);
        Assert.Equal(20, await PointsAsync(admin));
    }

    [Fact]
    public async Task A_family_task_can_move_to_a_personal_board_and_back()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Round trip", members: 1);
        var task = await CreateAsync(admin.Client, new { title = "Sort the garage" });

        var personal = await PostAsync(admin.Client, $"/api/tasks/{task.Id}/move-to-personal");
        Assert.Equal(admin.Id, personal.OwnerId);
        Assert.DoesNotContain(await BoardAsync(members[0].Client, "family"), t => t.Id == task.Id);

        var back = await PostAsync(admin.Client, $"/api/tasks/{task.Id}/move-to-family");
        Assert.Null(back.OwnerId);
        Assert.Contains(await BoardAsync(members[0].Client, "family"), t => t.Id == task.Id);

        // The family saw the task, so they see it move, without seeing what happened on the private board.
        var types = (await ActivityAsync(members[0].Client))
            .Where(a => a.Payload.TryGetProperty("taskId", out var id) && id.GetGuid() == task.Id)
            .Select(a => a.Type).Reverse();
        Assert.Equal(new[] { "TaskCreated", "TaskMadePersonal", "TaskSharedWithFamily" }, types);
    }

    [Fact]
    public async Task Moves_that_would_hide_work_or_points_are_refused()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Guarded", members: 1);
        var theirs = await CreateAsync(admin.Client, new { title = "Mow the lawn", assigneeId = members[0].Id });
        var done = await CreateAsync(admin.Client, new { title = "Bins", points = 5 });
        await PostAsync(admin.Client, $"/api/tasks/{done.Id}/complete");
        var mine = await CreateAsync(admin.Client, new { title = "Call the bank", personal = true });

        var takeTheirs = await admin.Client.PostAsync($"/api/tasks/{theirs.Id}/move-to-personal", null);
        var takeDone = await admin.Client.PostAsync($"/api/tasks/{done.Id}/move-to-personal", null);
        var assignMine = await admin.Client.PostAsJsonAsync($"/api/tasks/{mine.Id}/assign", new { assigneeId = members[0].Id });
        var createAssigned = await admin.Client.PostAsJsonAsync("/api/tasks",
            new { title = "Not yours", personal = true, assigneeId = members[0].Id });

        Assert.Equal(HttpStatusCode.Conflict, takeTheirs.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, takeDone.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, assignMine.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, createAssigned.StatusCode);
    }

    [Fact]
    public async Task An_unknown_board_is_a_bad_request()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Typo");

        var response = await admin.Client.GetAsync("/api/tasks?board=secret");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<TaskDto> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/tasks", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TaskDto>(ActivityLog.PayloadJson))!;
    }

    private static async Task<TaskDto> PostAsync(HttpClient client, string path)
    {
        var response = await client.PostAsync(path, null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TaskDto>(ActivityLog.PayloadJson))!;
    }

    private static async Task<List<TaskDto>> BoardAsync(HttpClient client, string? board) =>
        (await client.GetFromJsonAsync<List<TaskDto>>(board is null ? "/api/tasks" : $"/api/tasks?board={board}", ActivityLog.PayloadJson))!;

    private static async Task<int> PointsAsync(TestFamilies.TestUser user)
    {
        var rows = await user.Client.GetFromJsonAsync<List<LeaderboardRow>>("/api/points/leaderboard", ActivityLog.PayloadJson);
        return rows!.SingleOrDefault(r => r.UserId == user.Id)?.Points ?? 0;
    }

    private static async Task<List<ActivityEntry>> ActivityAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<ActivityEntry>>("/api/activity?limit=100", ActivityLog.PayloadJson))!;
}
