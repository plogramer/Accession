using Accession.Core.Model;

namespace Accession.Data.Audit;

/// <summary>Filter and keyset page for the Audit Log screen. Results are newest first.</summary>
public sealed record AuditQuery
{
    /// <summary>Inclusive lower bound.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Exclusive upper bound.</summary>
    public DateTimeOffset? To { get; init; }

    public IReadOnlyCollection<AuditAction>? Actions { get; init; }
    public string? UserName { get; init; }
    public string? MediaId { get; init; }

    /// <summary>Return rows older than this AuditId (the last AuditId of the previous page).</summary>
    public long? BeforeAuditId { get; init; }

    public int PageSize { get; init; } = 500;

    /// <summary>Rows to skip (for jumping to a page whose start is not known); prefer <see cref="BeforeAuditId"/>.</summary>
    public long Offset { get; init; }
}

/// <summary>
/// Writes and reads the audit trail (requirements 5.10). There are deliberately no update or delete methods:
/// audit rows are permanent, including after a media is deleted (AUD-03).
/// </summary>
public interface IAuditService
{
    /// <summary>Writes an entry in its own connection.</summary>
    void Write(AuditAction action, string? mediaId = null, object? details = null);

    /// <summary>Writes an entry in the caller's scope, joining its transaction if one is open.</summary>
    void Write(DbScope scope, AuditAction action, string? mediaId = null, object? details = null);

    /// <summary>Newest first.</summary>
    IReadOnlyList<AuditEntry> Query(AuditQuery query);

    /// <summary>Entries matching the filters of <paramref name="query"/> (paging fields are ignored).</summary>
    long Count(AuditQuery query);

    /// <summary>Distinct user names in the audit log, for the user filter.</summary>
    IReadOnlyList<string> ListUserNames();
}
