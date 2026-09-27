using Microsoft.Data.Sqlite;

namespace Accession.Data.Repositories;

/// <summary>Repositories work on a <see cref="DbScope"/> and join its transaction when one is open.</summary>
public abstract class RepositoryBase
{
    private readonly DbScope _scope;

    protected RepositoryBase(DbScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
    }

    protected SqliteConnection Connection => _scope.Connection;

    protected SqliteTransaction? Transaction => _scope.Transaction;
}
