namespace Accession.Core.Model;

/// <summary><c>File.HashStatus</c> values (SCN-31).</summary>
public enum HashStatus
{
    Pending = 0,
    Hashed = 1,
    Error = 2,

    /// <summary>Not hashed by design: the file is a reparse point (symbolic link), whose target may lie outside the media.</summary>
    Skipped = 3,
}
