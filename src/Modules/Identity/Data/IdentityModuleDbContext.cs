using FamilyApp.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyApp.Modules.Identity.Data;

/// <summary>
/// EF Core is used for queries and changes only. The schema itself comes from the SQL scripts
/// in <c>Migrations/</c> (ADR-021), so this mapping must match them; integration tests check it.
/// </summary>
public sealed class IdentityModuleDbContext(DbContextOptions<IdentityModuleDbContext> options, DbSession session)
    : ModuleDbContext(options, session)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<UserIdentity> UserIdentities => Set<UserIdentity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");

        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("users");
            user.HasKey(u => u.Id);
            user.Property(u => u.Id).HasColumnName("id").ValueGeneratedNever();
            user.Property(u => u.DisplayName).HasColumnName("display_name");
            user.Property(u => u.Email).HasColumnName("email");
            user.Property(u => u.AvatarUrl).HasColumnName("avatar_url");
            user.Property(u => u.CreatedAt).HasColumnName("created_at");
            user.Property(u => u.LastSeenAt).HasColumnName("last_seen_at");
        });

        modelBuilder.Entity<UserIdentity>(identity =>
        {
            identity.ToTable("user_identities");
            identity.HasKey(i => i.Id);
            identity.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
            identity.Property(i => i.UserId).HasColumnName("user_id");
            identity.Property(i => i.Provider).HasColumnName("provider");
            identity.Property(i => i.ProviderSubject).HasColumnName("provider_subject");
            identity.Property(i => i.Email).HasColumnName("email");
            identity.Property(i => i.CreatedAt).HasColumnName("created_at");

            // Declared so EF inserts the user before its identity in the same SaveChanges.
            identity.HasOne<User>().WithMany().HasForeignKey(i => i.UserId);
        });
    }
}

public sealed class User
{
    public Guid Id { get; set; }
    public required string DisplayName { get; set; }
    public string? Email { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

public sealed class UserIdentity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Provider { get; set; }
    public required string ProviderSubject { get; set; }
    public string? Email { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
