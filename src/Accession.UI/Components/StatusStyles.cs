namespace Accession.UI.Components;

/// <summary>CSS classes for status pills.</summary>
public static class StatusStyles
{
    /// <summary>Media status text (as shown) to a pill class.</summary>
    public static string ForMedia(string status) => status switch
    {
        "Completed" => "is-success",
        "Completed with errors" or "Incomplete" => "is-warning",
        "Missing" => "is-danger",
        "Scanning" or "Hashing" or "Queued" => "is-active",
        "Paused" => "is-paused",
        _ => "is-neutral",
    };

    /// <summary>Scan outcome text to a pill class.</summary>
    public static string ForOutcome(string outcome) => outcome switch
    {
        "Completed" => "is-success",
        "CompletedWithErrors" or "Interrupted" or "Cancelled" => "is-warning",
        "Failed" => "is-danger",
        "" => "is-active",
        _ => "is-neutral",
    };
}
