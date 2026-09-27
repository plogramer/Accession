using System.Globalization;
using Accession.Core.Settings;

namespace Accession.Core.Formatting;

/// <summary>Formats byte counts for display, e.g. <c>2.42 GB</c> or <c>2.20 GiB</c>.</summary>
public static class SizeFormatter
{
    private static readonly string[] DecimalUnits = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
    private static readonly string[] BinaryUnits = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB"];

    /// <summary>Scales <paramref name="bytes"/> to the largest unit where the value is at least 1.</summary>
    public static string Format(long bytes, SizeUnitSystem system, int decimals = 2, IFormatProvider? provider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);
        provider ??= CultureInfo.CurrentCulture;

        var (unitBase, units) = system == SizeUnitSystem.Binary ? (1024d, BinaryUnits) : (1000d, DecimalUnits);

        if (bytes < unitBase)
        {
            return string.Create(provider, $"{bytes:N0} {units[0]}");
        }

        var value = (double)bytes;
        var index = 0;
        while (value >= unitBase && index < units.Length - 1)
        {
            value /= unitBase;
            index++;
        }

        // Rounding can push e.g. 999.999 KB to "1,000.00 KB"; move up a unit instead.
        if (Math.Round(value, decimals) >= unitBase && index < units.Length - 1)
        {
            value /= unitBase;
            index++;
        }

        return value.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), provider) + " " + units[index];
    }
}
