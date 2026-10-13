using FamilyApp.SharedKernel;
using FamilyApp.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyApp.Modules.Points.Data;

/// <summary>
/// The points ledger (ADR-006). Reads are filtered to the current household like every
/// household-scoped context (ADR-028). Entries are written by event handlers with the household
/// from the event; the save guard checks it matches the request's household.
/// </summary>
public sealed class PointsDbContext(DbContextOptions<PointsDbContext> options, DbSession session, CurrentHousehold currentHousehold)
    : ModuleDbContext(options, session)
{
    public DbSet<LedgerEntry> Ledger => Set<LedgerEntry>();

    private Guid HouseholdId => currentHousehold.Id;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("points");

        modelBuilder.Entity<LedgerEntry>(entry =>
        {
            entry.ToTable("ledger");
            entry.HasKey(e => e.Id);
            entry.HasQueryFilter(e => e.HouseholdId == HouseholdId);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
            entry.Property(e => e.HouseholdId).HasColumnName("household_id");
            entry.Property(e => e.UserId).HasColumnName("user_id");
            entry.Property(e => e.TaskId).HasColumnName("task_id");
            entry.Property(e => e.Amount).HasColumnName("amount");
            entry.Property(e => e.Reason).HasColumnName("reason");
            entry.Property(e => e.Title).HasColumnName("title");
            entry.Property(e => e.OccurredAt).HasColumnName("occurred_at");
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<LedgerEntry>())
        {
            // Append-only: the ledger is never edited or deleted, only added to.
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Ledger entries are append-only; add a correcting entry instead.");
            }
            if (entry.State == EntityState.Added && entry.Entity.HouseholdId != HouseholdId)
            {
                throw new InvalidOperationException("Refusing to write points for another household.");
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}

public static class LedgerReasons
{
    public const string TaskCompleted = "task_completed";
    public const string TaskReopened = "task_reopened";
    public const string TaskDeleted = "task_deleted";
}

public sealed class LedgerEntry
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public Guid UserId { get; set; }
    public Guid? TaskId { get; set; }
    public int Amount { get; set; }
    public required string Reason { get; set; }
    public string? Title { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
