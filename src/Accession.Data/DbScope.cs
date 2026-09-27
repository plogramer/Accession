using Microsoft.Data.Sqlite;

namespace Accession.Data;

/// <summary>
/// An open connection plus an optional transaction. Repositories and services take a scope so several
/// operations can share one transaction (e.g. a media delete and its audit entry).
/// </summary>
public sealed class DbScope : IDisposable
{
    internal DbScope(SqliteConnection connection)
    {
        Connection = connection;
    }

    public SqliteConnection Connection { get; }

    /// <summary>The active transaction, or null when none is open.</summary>
    public SqliteTransaction? Transaction { get; private set; }

    /// <summary>Starts a write transaction (<c>BEGIN IMMEDIATE</c>). Commit it, or dispose to roll back.</summary>
    public DbTransactionScope BeginTransaction()
    {
        if (Transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already open on this scope.");
        }

        Transaction = Connection.BeginTransaction(deferred: false);
        return new DbTransactionScope(this);
    }

    internal void EndTransaction(bool commit)
    {
        if (Transaction is null)
        {
            return;
        }

        try
        {
            if (commit)
            {
                Transaction.Commit();
            }
            else
            {
                Transaction.Rollback();
            }
        }
        finally
        {
            Transaction.Dispose();
            Transaction = null;
        }
    }

    public void Dispose()
    {
        EndTransaction(commit: false);
        Connection.Dispose();
    }
}

/// <summary>Handle for a transaction started by <see cref="DbScope.BeginTransaction"/>.</summary>
public sealed class DbTransactionScope : IDisposable
{
    private readonly DbScope _scope;
    private bool _completed;

    internal DbTransactionScope(DbScope scope)
    {
        _scope = scope;
    }

    public void Commit()
    {
        if (_completed)
        {
            throw new InvalidOperationException("The transaction has already completed.");
        }

        _completed = true;
        _scope.EndTransaction(commit: true);
    }

    /// <summary>Rolls back unless <see cref="Commit"/> was called.</summary>
    public void Dispose()
    {
        if (!_completed)
        {
            _completed = true;
            _scope.EndTransaction(commit: false);
        }
    }
}
