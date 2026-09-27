using System.Globalization;
using Accession.Core.Settings;

namespace Accession.Core.Formatting;

/// <summary>Formats stored UTC timestamps for display in the selected time zone.</summary>
public static class TimeFormatter
{
    public static string Format(DateTimeOffset? value, DisplayTimeZone zone, IFormatProvider? provider = null)
    {
        if (value is null)
        {
            return string.Empty;
        }

        provider ??= CultureInfo.CurrentCulture;
        return zone == DisplayTimeZone.Utc
            ? value.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", provider) + " UTC"
            : value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", provider);
    }
}
