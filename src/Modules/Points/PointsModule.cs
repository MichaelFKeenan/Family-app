using FamilyApp.Modules.Points.Data;
using FamilyApp.SharedKernel;
using FamilyApp.SharedKernel.Events;
using FamilyApp.SharedKernel.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyApp.Modules.Points;

public sealed record LeaderboardEntry(Guid UserId, int Points);

public sealed record LedgerEntryDto(Guid Id, Guid UserId, Guid? TaskId, int Amount, string Reason, string? Title, DateTimeOffset OccurredAt);

/// <summary>
/// The points ledger and leaderboard (ADR-006, ADR-016, ADR-032). The module has no idea tasks
/// exist beyond the events it hears; it never calls the Tasks module.
/// </summary>
public sealed class PointsModule : IModule
{
    public string Name => "points";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PointsDbContext>();
        services.AddScoped<IDomainEventHandler, AwardPointsOnCompletion>();
        services.AddScoped<IDomainEventHandler, ReversePoints>();
        services.AddScoped<IDomainEventHandler, ReversePointsOnDelete>();
    }

    public void MapEndpoints(IEndpointRouteBuilder group)
    {
        var points = group.MapGroup("").RequireHousehold();

        // Totals per person between from (inclusive) and to (exclusive). Both optional.
        // The client works out "this week" and "this month" in its own time zone (ADR-032).
        points.MapGet("/leaderboard", async (DateTimeOffset? from, DateTimeOffset? to, PointsDbContext db, CancellationToken ct) =>
        {
            var entries = db.Ledger.AsQueryable();
            if (from is not null) entries = entries.Where(e => e.OccurredAt >= from.Value.ToUniversalTime());
            if (to is not null) entries = entries.Where(e => e.OccurredAt < to.Value.ToUniversalTime());

            var totals = await entries
                .GroupBy(e => e.UserId)
                .Select(g => new LeaderboardEntry(g.Key, g.Sum(e => e.Amount)))
                .ToListAsync(ct);

            return totals.Where(t => t.Points != 0).OrderByDescending(t => t.Points).ToList();
        });

        // The ledger itself, newest first: where everyone's points came from.
        points.MapGet("/history", async (Guid? userId, int? limit, PointsDbContext db, CancellationToken ct) =>
        {
            var entries = db.Ledger.AsNoTracking();
            if (userId is not null) entries = entries.Where(e => e.UserId == userId);

            return await entries
                .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
                .Take(Math.Clamp(limit ?? 50, 1, 200))
                .Select(e => new LedgerEntryDto(e.Id, e.UserId, e.TaskId, e.Amount, e.Reason, e.Title, e.OccurredAt))
                .ToListAsync(ct);
        });
    }
}
