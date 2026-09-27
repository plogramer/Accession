namespace Accession.Core.Model;

/// <summary>Lifecycle status of a media (requirements 5.4). Stored as text.</summary>
public enum MediaStatus
{
    New,
    Queued,
    Scanning,
    Hashing,
    Paused,
    Completed,
    CompletedWithErrors,
    Incomplete,
    Missing,
}
