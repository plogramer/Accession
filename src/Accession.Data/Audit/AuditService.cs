using System.Text;
using System.Text.Json;
using Accession.Core.Model;
using Accession.Core.Runtime;
using Dapper;

namespace Accession.Data.Audit;

public sealed class AuditService : IAuditService
{
    private static readonly JsonSerializerOptions DetailsJson = new(JsonSerializerDefaults.Web);

    private readonly InventoryDatabase _database;
    private readonly IUserContext _user;
    private readonly TimeProvider _timeProvider;

    public AuditService(InventoryDatabase database, IUserContext user, TimeProvider timeProvider)
    {
        _database = database;
        _user = user;
        _timeProvider = timeProvider;
    }

    public void Write(AuditAction action, string? mediaId = null, object? details = null)
    {
        using var scope = _database.Open();
        Write(scope, action, mediaId, details);
    }

    public void Write(DbScope scope, AuditAction action, string? mediaId = null, object? details = null)
    {
        ArgumentNullException.ThrowIfNull(scope);

        scope.Connection.Execute(
            """
            INSERT INTO AuditLog (OccurredAtUtc, UserName, MachineName, Action, MediaId, Details)
            VALUES (@occurredAt, @userName, @machineName, @action, @mediaId, @details)
            """,
            new
            {
                occurredAt = _timeProvider.GetUtcNow(),
                userName = _user.UserName,
                machineName = _user.MachineName,
                action = action.ToString(),
                mediaId,
                details = details is null ? null : JsonSerializer.Serialize(details, details.GetType(), DetailsJson),
            },
            scope.Transaction);
    }

    public IReadOnlyList<AuditEntry> Query(AuditQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1);

        var (where, parameters) = BuildWhere(query);
        if (query.BeforeAuditId is not null)
        {
            where.Append(" AND AuditId < @BeforeAuditId");
            parameters.Add("BeforeAuditId", query.BeforeAuditId);
        }

        parameters.Add("PageSize", query.PageSize);
        parameters.Add("Offset", query.Offset);
        using var scope = _database.Open();
        return scope.Connection.Query<AuditEntry>(
            $"SELECT * FROM AuditLog WHERE {where} ORDER BY AuditId DESC LIMIT @PageSize OFFSET @Offset", parameters).AsList();
    }

    public long Count(AuditQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (where, parameters) = BuildWhere(query);
        using var scope = _database.Open();
        return scope.Connection.ExecuteScalar<long>($"SELECT COUNT(*) FROM AuditLog WHERE {where}", parameters);
    }

    /// <summary>Filters shared by <see cref="Query"/> and <see cref="Count"/> (paging fields excluded).</summary>
    private static (StringBuilder Where, DynamicParameters Parameters) BuildWhere(AuditQuery query)
    {
        var where = new StringBuilder("1 = 1");
        var parameters = new DynamicParameters();
        if (query.From is not null)
        {
            where.Append(" AND OccurredAtUtc >= @From");
            parameters.Add("From", query.From.Value);
        }

        if (query.To is not null)
        {
            where.Append(" AND OccurredAtUtc < @To");
            parameters.Add("To", query.To.Value);
        }

        if (query.Actions is { Count: > 0 })
        {
            where.Append(" AND Action IN @Actions");
            parameters.Add("Actions", query.Actions.Select(a => a.ToString()).ToList());
        }

        if (!string.IsNullOrWhiteSpace(query.UserName))
        {
            where.Append(" AND UserName = @UserName COLLATE NOCASE");
            parameters.Add("UserName", query.UserName);
        }

        if (!string.IsNullOrWhiteSpace(query.MediaId))
        {
            where.Append(" AND MediaId = @MediaId COLLATE NOCASE");
            parameters.Add("MediaId", query.MediaId);
        }

        return (where, parameters);
    }

    public IReadOnlyList<string> ListUserNames()
    {
        using var scope = _database.Open();
        return scope.Connection.Query<string>("SELECT DISTINCT UserName FROM AuditLog ORDER BY UserName COLLATE NOCASE").AsList();
    }
}
