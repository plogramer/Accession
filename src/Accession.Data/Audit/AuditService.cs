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

        var sql = new StringBuilder("SELECT * FROM AuditLog WHERE 1 = 1");
        var parameters = new DynamicParameters();
        if (query.From is not null)
        {
            sql.Append(" AND OccurredAtUtc >= @From");
            parameters.Add("From", query.From.Value);
        }

        if (query.To is not null)
        {
            sql.Append(" AND OccurredAtUtc < @To");
            parameters.Add("To", query.To.Value);
        }

        if (query.Actions is { Count: > 0 })
        {
            sql.Append(" AND Action IN @Actions");
            parameters.Add("Actions", query.Actions.Select(a => a.ToString()).ToList());
        }

        if (!string.IsNullOrWhiteSpace(query.UserName))
        {
            sql.Append(" AND UserName = @UserName COLLATE NOCASE");
            parameters.Add("UserName", query.UserName);
        }

        if (!string.IsNullOrWhiteSpace(query.MediaId))
        {
            sql.Append(" AND MediaId = @MediaId COLLATE NOCASE");
            parameters.Add("MediaId", query.MediaId);
        }

        if (query.BeforeAuditId is not null)
        {
            sql.Append(" AND AuditId < @BeforeAuditId");
            parameters.Add("BeforeAuditId", query.BeforeAuditId);
        }

        sql.Append(" ORDER BY AuditId DESC LIMIT @PageSize");
        parameters.Add("PageSize", query.PageSize);

        using var scope = _database.Open();
        return scope.Connection.Query<AuditEntry>(sql.ToString(), parameters).AsList();
    }

    public IReadOnlyList<string> ListUserNames()
    {
        using var scope = _database.Open();
        return scope.Connection.Query<string>("SELECT DISTINCT UserName FROM AuditLog ORDER BY UserName COLLATE NOCASE").AsList();
    }
}
