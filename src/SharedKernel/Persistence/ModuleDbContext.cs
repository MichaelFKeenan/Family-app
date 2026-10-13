using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyApp.SharedKernel.Persistence;

/// <summary>
/// Base class for every module's DbContext: it runs on the request's shared connection and
/// joins the request's transaction when there is one (ADR-030).
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options, DbSession session) : DbContext(options)
{
    /// <summary>Joins the request transaction. Call before raw SQL or bulk updates in a write.</summary>
    public void JoinRequestTransaction() => session.Enlist(Database);

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        JoinRequestTransaction();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

public static class ModuleDbContextRegistration
{
    /// <summary>Registers a module DbContext on the request's shared connection.</summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services)
        where TContext : ModuleDbContext =>
        services.AddDbContext<TContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<DbSession>().Connection));
}
