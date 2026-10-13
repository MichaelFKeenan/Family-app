using FamilyApp.SharedKernel;
using FamilyApp.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyApp.Modules.Tasks.Data;

/// <summary>
/// The first household-scoped DbContext (ADR-009). Every query is filtered to the request's
/// <see cref="CurrentHousehold"/> by a global query filter, and every new row is stamped with it,
/// so household isolation doesn't depend on each query remembering a WHERE clause.
/// If no household was resolved for the request, any query throws: it fails closed.
/// </summary>
public sealed class TasksDbContext(DbContextOptions<TasksDbContext> options, DbSession session, CurrentHousehold currentHousehold)
    : ModuleDbContext(options, session)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Category> Categories => Set<Category>();

    // Read by the query filters. EF Core evaluates context members per query, not once per model,
    // so each request filters by its own household.
    private Guid HouseholdId => currentHousehold.Id;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("tasks");

        modelBuilder.Entity<Category>(category =>
        {
            category.ToTable("categories");
            category.HasKey(c => c.Id);
            category.HasQueryFilter(c => c.HouseholdId == HouseholdId);
            category.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
            category.Property(c => c.HouseholdId).HasColumnName("household_id");
            category.Property(c => c.Name).HasColumnName("name");
            category.Property(c => c.SortOrder).HasColumnName("sort_order");
            category.Property(c => c.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<TaskItem>(task =>
        {
            task.ToTable("tasks");
            task.HasKey(t => t.Id);
            task.HasQueryFilter(t => t.HouseholdId == HouseholdId);
            task.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
            task.Property(t => t.HouseholdId).HasColumnName("household_id");
            task.Property(t => t.Title).HasColumnName("title");
            task.Property(t => t.Description).HasColumnName("description");
            task.Property(t => t.Status).HasColumnName("status").HasConversion(
                status => status.ToString().ToLowerInvariant(),
                value => Enum.Parse<TaskState>(value, ignoreCase: true));
            task.Property(t => t.Priority).HasColumnName("priority").HasConversion(
                priority => priority.ToString().ToLowerInvariant(),
                value => Enum.Parse<TaskPriority>(value, ignoreCase: true));
            task.Property(t => t.Points).HasColumnName("points");
            task.Property(t => t.CategoryId).HasColumnName("category_id");
            task.Property(t => t.AssigneeId).HasColumnName("assignee_id");
            task.Property(t => t.DueOn).HasColumnName("due_on");
            task.Property(t => t.CreatedBy).HasColumnName("created_by");
            task.Property(t => t.CreatedAt).HasColumnName("created_at");
            task.Property(t => t.UpdatedAt).HasColumnName("updated_at");
            task.Property(t => t.CompletedAt).HasColumnName("completed_at");
            task.Property(t => t.CompletedBy).HasColumnName("completed_by");

            // EF adds "AND version = <value we loaded>" to every UPDATE and DELETE, and throws
            // DbUpdateConcurrencyException when no row matches (ADR-014).
            task.Property(t => t.Version).HasColumnName("version").IsConcurrencyToken();
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<IHouseholdOwned>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.HouseholdId = HouseholdId;
            }
            else if ((entry.State is EntityState.Modified or EntityState.Deleted) && entry.Entity.HouseholdId != HouseholdId)
            {
                // Can't happen through the filtered queries; this is the backstop if code ever bypasses them.
                throw new InvalidOperationException("Refusing to change a row that belongs to another household.");
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}

public interface IHouseholdOwned
{
    Guid HouseholdId { get; set; }
}

public enum TaskState
{
    Todo,
    Doing,
    Done,
}

public enum TaskPriority
{
    Low,
    Medium,
    High,
    Urgent,
}

public sealed class TaskItem : IHouseholdOwned
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public TaskState Status { get; set; }
    public TaskPriority Priority { get; set; }
    public int Points { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? AssigneeId { get; set; }
    public DateOnly? DueOn { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? CompletedBy { get; set; }
    public int Version { get; set; }
}

public sealed class Category : IHouseholdOwned
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
