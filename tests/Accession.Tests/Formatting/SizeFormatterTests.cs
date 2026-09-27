using System.Globalization;
using Accession.Core.Formatting;
using Accession.Core.Settings;

namespace Accession.Tests.Formatting;

public class SizeFormatterTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(999L, "999 B")]
    [InlineData(1_000L, "1.00 KB")]
    [InlineData(1_500L, "1.50 KB")]
    [InlineData(999_999L, "1.00 MB")]
    [InlineData(2_418_000_000_000L, "2.42 TB")]
    [InlineData(5_000_000_000L, "5.00 GB")]
    [InlineData(long.MaxValue, "9.22 EB")]
    public void Decimal_units(long bytes, string expected)
    {
        Assert.Equal(expected, SizeFormatter.Format(bytes, SizeUnitSystem.Decimal, provider: Invariant));
    }

    [Theory]
    [InlineData(1_023L, "1,023 B")]
    [InlineData(1_024L, "1.00 KiB")]
    [InlineData(1_048_576L, "1.00 MiB")]
    [InlineData(5_000_000_000L, "4.66 GiB")]
    [InlineData(1_099_511_627_776L, "1.00 TiB")]
    public void Binary_units(long bytes, string expected)
    {
        Assert.Equal(expected, SizeFormatter.Format(bytes, SizeUnitSystem.Binary, provider: Invariant));
    }

    [Fact]
    public void Decimals_can_be_changed()
    {
        Assert.Equal("2 GB", SizeFormatter.Format(1_600_000_000, SizeUnitSystem.Decimal, decimals: 0, provider: Invariant));
        Assert.Equal("1.600 GB", SizeFormatter.Format(1_600_000_000, SizeUnitSystem.Decimal, decimals: 3, provider: Invariant));
    }

    [Fact]
    public void Negative_size_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SizeFormatter.Format(-1, SizeUnitSystem.Decimal));
    }
}
