using FamilyApp.SharedKernel;
using FamilyApp.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyApp.Modules.Shopping.Data;

/// <summary>
/// Shopping items, filtered to the request's household like every household-scoped context
/// (ADR-009, ADR-028). New rows are stamped with the household; changes to another household's
/// rows are refused.
/// </summary>
public sealed class ShoppingDbContext(DbContextOptions<ShoppingDbContext> options, DbSession session, CurrentHousehold currentHousehold)
    : ModuleDbContext(options, session)
{
    public DbSet<ShoppingItem> Items => Set<ShoppingItem>();

    internal Guid HouseholdId => currentHousehold.Id;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("shopping");

        modelBuilder.Entity<ShoppingItem>(item =>
        {
            item.ToTable("items");
            item.HasKey(i => i.Id);
            item.HasQueryFilter(i => i.HouseholdId == HouseholdId);
            item.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
            item.Property(i => i.HouseholdId).HasColumnName("household_id");
            item.Property(i => i.Name).HasColumnName("name");
            item.Property(i => i.Quantity).HasColumnName("quantity");
            item.Property(i => i.Note).HasColumnName("note");
            item.Property(i => i.AddedBy).HasColumnName("added_by");
            item.Property(i => i.AddedAt).HasColumnName("added_at");
            item.Property(i => i.CheckedBy).HasColumnName("checked_by");
            item.Property(i => i.CheckedAt).HasColumnName("checked_at");
            item.Property(i => i.CheckReason).HasColumnName("check_reason").HasConversion(
                reason => reason == null ? null : reason == Data.CheckReason.NotNeeded ? "not_needed" : "bought",
                value => value == null ? null : value == "not_needed" ? Data.CheckReason.NotNeeded : Data.CheckReason.Bought);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<ShoppingItem>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.HouseholdId = HouseholdId;
            }
            else if ((entry.State is EntityState.Modified or EntityState.Deleted) && entry.Entity.HouseholdId != HouseholdId)
            {
                throw new InvalidOperationException("Refusing to change a row that belongs to another household.");
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ShoppingItem
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public required string Name { get; set; }
    public string? Quantity { get; set; }
    public string? Note { get; set; }
    public Guid AddedBy { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public Guid? CheckedBy { get; set; }
    public DateTimeOffset? CheckedAt { get; set; }
    public CheckReason? CheckReason { get; set; }
}

/// <summary>Why an item left the list (ADR-038).</summary>
public enum CheckReason
{
    Bought,
    NotNeeded,
}
