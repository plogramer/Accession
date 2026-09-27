namespace Accession.Core.Model;

/// <summary>File count and bytes for one extension of one media (<c>MediaExtensionSummary</c>).</summary>
public sealed record ExtensionSummary(long MediaKey, string Extension, long FileCount, long TotalBytes);
