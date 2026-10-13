using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace FamilyApp.SharedKernel.Persistence;

/// <summary>
/// One database connection per request, shared by every module's DbContext (ADR-030).
///
/// Modules keep separate DbContexts and schemas, but a write request runs in a single Postgres
/// transaction on this one connection. That is what lets "complete the task" in Tasks and
/// "award the points" in Points commit or roll back together, without the modules knowing
/// about each other.
/// </summary>
public sealed class DbSession(NpgsqlDataSource dataSource) : IAsyncDisposable
{
    private NpgsqlConnection? _connection;

    /// <summary>Created on first use, opened by EF Core or by <see cref="BeginAsync"/>.</summary>
    public NpgsqlConnection Connection => _connection ??= dataSource.CreateConnection();

    public NpgsqlTransaction? Transaction { get; private set; }

    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        if (Transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already in progress for this request.");
        }
        if (Connection.State != System.Data.ConnectionState.Open)
        {
            await Connection.OpenAsync(cancellationToken);
        }
        Transaction = await Connection.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (Transaction is null) return;
        await Transaction.CommitAsync(cancellationToken);
        await Transaction.DisposeAsync();
        Transaction = null;
    }

    public async Task RollbackAsync()
    {
        if (Transaction is null) return;
        await Transaction.RollbackAsync();
        await Transaction.DisposeAsync();
        Transaction = null;
    }

    /// <summary>
    /// Makes a DbContext use the request's transaction. Without this, EF Core would try to
    /// start its own transaction on SaveChanges, and Postgres doesn't allow nested ones.
    /// </summary>
    public void Enlist(DatabaseFacade database)
    {
        if (Transaction is not null && database.CurrentTransaction is null)
        {
            database.UseTransaction(Transaction);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await RollbackAsync();
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
