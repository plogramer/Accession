using System.Diagnostics;
using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Accession.Data.Queries;

/// <summary>
/// Runs named SQL files of a query set (e.g. "Dashboard"). A file in the override directory replaces the
/// embedded one with the same name, so queries can be changed without rebuilding (requirement DSH-10).
/// </summary>
public sealed class QuerySetRunner
{
    private readonly string _setName;
    private readonly string? _overrideDirectory;
    private readonly ILogger _logger;

    public QuerySetRunner(string setName, string? overrideDirectory, ILogger logger)
    {
        _setName = setName;
        _overrideDirectory = overrideDirectory;
        _logger = logger;
    }

    /// <summary>SQL text of <paramref name="name"/> (without ".sql"): override file if present, else embedded.</summary>
    public string LoadSql(string name)
    {
        if (_overrideDirectory is not null)
        {
            var file = Path.Combine(_overrideDirectory, name + ".sql");
            if (File.Exists(file))
            {
                return File.ReadAllText(file);
            }
        }

        var resource = $"Accession.Data.Queries.{_setName}.{name}.sql";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Query '{resource}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>True when an override file is used for <paramref name="name"/>.</summary>
    public bool IsOverridden(string name) =>
        _overrideDirectory is not null && File.Exists(Path.Combine(_overrideDirectory, name + ".sql"));

    public IReadOnlyList<T> Query<T>(DbScope scope, string name, object? parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var stopwatch = Stopwatch.StartNew();
        var rows = scope.Connection.Query<T>(new CommandDefinition(
            LoadSql(name), parameters, scope.Transaction, commandTimeout: 0, cancellationToken: cancellationToken)).AsList();
        _logger.LogDebug("Query {Set}/{Name} returned {Rows} rows in {Elapsed} ms", _setName, name, rows.Count, stopwatch.ElapsedMilliseconds);
        return rows;
    }
}
