using System.Globalization;

namespace Accession.Core.Time;

/// <summary>
/// The one timestamp format used in inventory databases: UTC ISO-8601 with 100 ns precision,
/// e.g. <c>2026-09-27T14:03:12.1234567Z</c>. Sortable as text and readable by SQLite date functions.
/// </summary>
public static class UtcTimestamp
{
    public const string Format = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    public static string ToText(DateTimeOffset value) =>
        value.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture);

    public static string ToText(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? throw new ArgumentException("DateTime kind must be Utc or Local.", nameof(value))
            : value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture);

    public static DateTimeOffset Parse(string text) =>
        DateTimeOffset.ParseExact(text, Format, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    public static bool TryParse(string? text, out DateTimeOffset value) =>
        DateTimeOffset.TryParseExact(text, Format, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);
}
