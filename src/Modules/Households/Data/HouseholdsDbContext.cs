using Microsoft.EntityFrameworkCore;

namespace FamilyApp.Modules.Households.Data;

/// <summary>
/// Mapped by hand to the SQL in <c>Migrations/</c> (ADR-021).
///
/// Unlike the modules that will follow, this context has no global household filter: it is the
/// module that decides which households a user belongs to, so its queries are by user or by an
/// explicit household id. Tasks, Shopping and Points will filter on <c>CurrentHousehold</c>.
/// </summary>
public sealed class HouseholdsDbContext(DbContextOptions<HouseholdsDbContext> options) : DbContext(options)
{
    public DbSet<Household> Households => Set<Household>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Invite> Invites => Set<Invite>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("households");

        modelBuilder.Entity<Household>(household =>
        {
            household.ToTable("households");
            household.HasKey(h => h.Id);
            household.Property(h => h.Id).HasColumnName("id").ValueGeneratedNever();
            household.Property(h => h.Name).HasColumnName("name");
            household.Property(h => h.CreatedBy).HasColumnName("created_by");
            household.Property(h => h.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<Membership>(membership =>
        {
            membership.ToTable("memberships");
            membership.HasKey(m => new { m.HouseholdId, m.UserId });
            membership.Property(m => m.HouseholdId).HasColumnName("household_id");
            membership.Property(m => m.UserId).HasColumnName("user_id");
            membership.Property(m => m.Role).HasColumnName("role");
            membership.Property(m => m.JoinedAt).HasColumnName("joined_at");
            membership.HasOne<Household>().WithMany().HasForeignKey(m => m.HouseholdId);
        });

        modelBuilder.Entity<Invite>(invite =>
        {
            invite.ToTable("invites");
            invite.HasKey(i => i.Id);
            invite.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
            invite.Property(i => i.HouseholdId).HasColumnName("household_id");
            invite.Property(i => i.CodeHash).HasColumnName("code_hash");
            invite.Property(i => i.CreatedBy).HasColumnName("created_by");
            invite.Property(i => i.CreatedAt).HasColumnName("created_at");
            invite.Property(i => i.ExpiresAt).HasColumnName("expires_at");
            invite.Property(i => i.UsedAt).HasColumnName("used_at");
            invite.Property(i => i.UsedBy).HasColumnName("used_by");
            invite.HasOne<Household>().WithMany().HasForeignKey(i => i.HouseholdId);
        });
    }
}

public sealed class Household
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Membership
{
    public const string Admin = "admin";
    public const string Member = "member";

    public Guid HouseholdId { get; set; }
    public Guid UserId { get; set; }
    public required string Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}

public sealed class Invite
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public required byte[] CodeHash { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public Guid? UsedBy { get; set; }
}
