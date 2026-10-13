using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FamilyApp.Host.Events;
using FamilyApp.Modules.Points;
using FamilyApp.Modules.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyApp.Integration.Tests;

public class PointsTests(FamilyAppFactory factory) : IClassFixture<FamilyAppFactory>
{
    private static readonly JsonSerializerOptions Json = ActivityLog.PayloadJson;

    [Fact]
    public async Task An_unassigned_task_pays_whoever_completes_it()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Unassigned", members: 1);
        var task = await CreateTaskAsync(admin.Client, "Empty the dishwasher", points: 5);

        await Post(members[0].Client, $"/api/tasks/{task.Id}/complete");

        Assert.Equal(new Dictionary<Guid, int> { [members[0].Id] = 5 }, await LeaderboardAsync(admin.Client));
    }

    [Fact]
    public async Task An_assigned_task_pays_the_assignee_even_if_someone_else_completes_it()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Assigned", members: 1);
        var task = await CreateTaskAsync(admin.Client, "Hoover the stairs", points: 10, assigneeId: members[0].Id);

        await Post(admin.Client, $"/api/tasks/{task.Id}/complete");

        Assert.Equal(new Dictionary<Guid, int> { [members[0].Id] = 10 }, await LeaderboardAsync(admin.Client));
    }

    [Fact]
    public async Task Zero_point_tasks_add_nothing_to_the_ledger()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Freebies");
        var task = await CreateTaskAsync(admin.Client, "Water the plants", points: 0);

        await Post(admin.Client, $"/api/tasks/{task.Id}/complete");

        Assert.Empty(await HistoryAsync(admin.Client));
    }

    [Fact]
    public async Task Reopening_takes_points_back_and_completing_again_pays_the_current_value()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Reopen");
        var task = await CreateTaskAsync(admin.Client, "Sort the recycling", points: 5);

        await Post(admin.Client, $"/api/tasks/{task.Id}/complete");
        var reopened = await Post(admin.Client, $"/api/tasks/{task.Id}/reopen");
        Assert.Empty(await LeaderboardAsync(admin.Client));

        // Worth more now; the earlier award isn't rewritten, the new completion pays the new value.
        (await admin.Client.PutAsJsonAsync($"/api/tasks/{task.Id}", new { title = task.Title, points = 8, version = reopened.Version }, Json)).EnsureSuccessStatusCode();
        await Post(admin.Client, $"/api/tasks/{task.Id}/complete");

        Assert.Equal(new Dictionary<Guid, int> { [admin.Id] = 8 }, await LeaderboardAsync(admin.Client));
        Assert.Equal(new[] { 8, -5, 5 }, (await HistoryAsync(admin.Client)).Select(e => e.Amount));
    }

    [Fact]
    public async Task Reversal_follows_the_ledger_even_after_reassignment()
    {
        // Points went to the first assignee. Reassigning the done task and reopening it must take
        // them back from the person who actually holds them, not from the new assignee.
        var (_, admin, members) = await factory.NewHouseholdAsync("Reassigned", members: 2);
        var task = await CreateTaskAsync(admin.Client, "Mow the lawn", points: 20, assigneeId: members[0].Id);

        await Post(admin.Client, $"/api/tasks/{task.Id}/complete");
        (await admin.Client.PostAsJsonAsync($"/api/tasks/{task.Id}/assign", new { assigneeId = members[1].Id })).EnsureSuccessStatusCode();
        await Post(admin.Client, $"/api/tasks/{task.Id}/reopen");

        Assert.Empty(await LeaderboardAsync(admin.Client));
    }

    [Fact]
    public async Task Deleting_a_done_task_takes_its_points_back_but_deleting_an_open_one_changes_nothing()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Deleting");
        var done = await CreateTaskAsync(admin.Client, "Done then deleted", points: 7);
        var open = await CreateTaskAsync(admin.Client, "Never done", points: 9);
        await Post(admin.Client, $"/api/tasks/{done.Id}/complete");

        (await admin.Client.DeleteAsync($"/api/tasks/{done.Id}")).EnsureSuccessStatusCode();
        (await admin.Client.DeleteAsync($"/api/tasks/{open.Id}")).EnsureSuccessStatusCode();

        Assert.Empty(await LeaderboardAsync(admin.Client));
        Assert.Equal(new[] { "task_deleted", "task_completed" }, (await HistoryAsync(admin.Client)).Select(e => e.Reason));
    }

    [Fact]
    public async Task Simultaneous_completions_pay_out_once()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Paid once", members: 3);
        var task = await CreateTaskAsync(admin.Client, "Take the bins out", points: 15);

        await Task.WhenAll(members.Append(admin).Select(u => u.Client.PostAsync($"/api/tasks/{task.Id}/complete", null)));

        Assert.Equal(15, (await LeaderboardAsync(admin.Client)).Values.Sum());
        Assert.Single(await HistoryAsync(admin.Client));
    }

    [Fact]
    public async Task The_leaderboard_can_be_limited_to_a_period()
    {
        var (household, admin, _) = await factory.NewHouseholdAsync("Periods");
        var old = await CreateTaskAsync(admin.Client, "Last month's job", points: 30);
        var recent = await CreateTaskAsync(admin.Client, "This week's job", points: 4);
        await Post(admin.Client, $"/api/tasks/{old.Id}/complete");
        await Post(admin.Client, $"/api/tasks/{recent.Id}/complete");

        await using (var command = factory.Services.GetRequiredService<NpgsqlDataSource>().CreateCommand(
            "UPDATE points.ledger SET occurred_at = now() - interval '40 days' WHERE household_id = $1 AND task_id = $2"))
        {
            command.Parameters.Add(new NpgsqlParameter { Value = household.Id });
            command.Parameters.Add(new NpgsqlParameter { Value = old.Id });
            await command.ExecuteNonQueryAsync();
        }

        var weekAgo = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-7).ToString("O"));
        var thisWeek = await LeaderboardAsync(admin.Client, $"?from={weekAgo}");
        var allTime = await LeaderboardAsync(admin.Client);

        Assert.Equal(4, thisWeek[admin.Id]);
        Assert.Equal(34, allTime[admin.Id]);
    }

    [Fact]
    public async Task Points_are_private_to_the_household()
    {
        var (_, smith, _) = await factory.NewHouseholdAsync("Point Smiths");
        var (_, jones, _) = await factory.NewHouseholdAsync("Point Joneses");
        var task = await CreateTaskAsync(smith.Client, "Smiths' chore", points: 50);
        await Post(smith.Client, $"/api/tasks/{task.Id}/complete");

        Assert.Empty(await LeaderboardAsync(jones.Client));
        Assert.Empty(await HistoryAsync(jones.Client));
    }

    [Fact]
    public async Task Points_changes_appear_in_the_activity_log()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Logged points", members: 1);
        var task = await CreateTaskAsync(admin.Client, "Bake a cake", points: 12, assigneeId: members[0].Id);
        await Post(admin.Client, $"/api/tasks/{task.Id}/complete");

        var activity = await admin.Client.GetFromJsonAsync<List<ActivityEntry>>("/api/activity?limit=100", Json);
        var awarded = Assert.Single(activity!, a => a.Type == "PointsAwarded");

        Assert.Equal(admin.Id, awarded.ActorId);
        Assert.Equal(members[0].Id, awarded.Payload.GetProperty("recipientId").GetGuid());
        Assert.Equal(12, awarded.Payload.GetProperty("amount").GetInt32());
    }

    [Fact]
    public async Task The_backfill_rebuilds_points_from_the_activity_log()
    {
        // Simulated history for a household that completed tasks before points existed.
        var household = Guid.CreateVersion7();
        var (alex, sam) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var (stillDone, reopened, deleted, assigned) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        var dataSource = factory.Services.GetRequiredService<NpgsqlDataSource>();
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var at = DateTimeOffset.UtcNow.AddDays(-1);
        await LogAsync("TaskCompleted", alex, new { taskId = stillDone, title = "Still done", points = 5, assigneeId = (Guid?)null });
        await LogAsync("TaskCompleted", alex, new { taskId = reopened, title = "Reopened", points = 7, assigneeId = (Guid?)null });
        await LogAsync("TaskReopened", alex, new { taskId = reopened, title = "Reopened", wasDone = true });
        await LogAsync("TaskCompleted", alex, new { taskId = deleted, title = "Deleted", points = 9, assigneeId = (Guid?)null });
        await LogAsync("TaskDeleted", alex, new { taskId = deleted, title = "Deleted", wasDone = true });
        await LogAsync("TaskCompleted", alex, new { taskId = assigned, title = "Sam's job", points = 3, assigneeId = (Guid?)sam });

        string script;
        await using (var stream = typeof(PointsModule).Assembly.GetManifestResourceStream("migrations/0002_backfill_from_activity_log.sql")!)
        using (var reader = new StreamReader(stream))
        {
            script = await reader.ReadToEndAsync();
        }
        await using (var run = new NpgsqlCommand(script, connection, transaction))
        {
            await run.ExecuteNonQueryAsync();
        }

        var totals = new Dictionary<Guid, int>();
        await using (var query = new NpgsqlCommand(
            "SELECT user_id, sum(amount)::int FROM points.ledger WHERE household_id = $1 GROUP BY user_id", connection, transaction))
        {
            query.Parameters.Add(new NpgsqlParameter { Value = household });
            await using var rows = await query.ExecuteReaderAsync();
            while (await rows.ReadAsync()) totals[rows.GetGuid(0)] = rows.GetInt32(1);
        }

        await transaction.RollbackAsync();

        Assert.Equal(new Dictionary<Guid, int> { [alex] = 5, [sam] = 3 }, totals);

        async Task LogAsync(string type, Guid actor, object payload)
        {
            at = at.AddMinutes(1);
            await using var insert = new NpgsqlCommand(
                "INSERT INTO platform.activity_events (id, household_id, actor_id, type, payload, occurred_at) VALUES ($1, $2, $3, $4, $5::jsonb, $6)",
                connection, transaction);
            insert.Parameters.Add(new NpgsqlParameter { Value = Guid.CreateVersion7() });
            insert.Parameters.Add(new NpgsqlParameter { Value = household });
            insert.Parameters.Add(new NpgsqlParameter { Value = actor });
            insert.Parameters.Add(new NpgsqlParameter { Value = type });
            insert.Parameters.Add(new NpgsqlParameter { Value = JsonSerializer.Serialize(payload, Json) });
            insert.Parameters.Add(new NpgsqlParameter { Value = at });
            await insert.ExecuteNonQueryAsync();
        }
    }

    private static async Task<TaskDto> CreateTaskAsync(HttpClient client, string title, int points, Guid? assigneeId = null)
    {
        var response = await client.PostAsJsonAsync("/api/tasks", new { title, points, assigneeId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskDto>(Json))!;
    }

    private static async Task<TaskDto> Post(HttpClient client, string path)
    {
        var response = await client.PostAsync(path, null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TaskDto>(Json))!;
    }

    private static async Task<Dictionary<Guid, int>> LeaderboardAsync(HttpClient client, string query = "") =>
        (await client.GetFromJsonAsync<List<LeaderboardEntry>>($"/api/points/leaderboard{query}", Json))!
            .ToDictionary(e => e.UserId, e => e.Points);

    private static async Task<List<LedgerEntryDto>> HistoryAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<LedgerEntryDto>>("/api/points/history", Json))!;
}
