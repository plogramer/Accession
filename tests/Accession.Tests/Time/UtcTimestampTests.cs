using Accession.Core.Time;

namespace Accession.Tests.Time;

public class UtcTimestampTests
{
    [Fact]
    public void Formats_utc_with_seven_fraction_digits()
    {
        var value = new DateTimeOffset(2026, 9, 27, 14, 3, 12, TimeSpan.Zero).AddTicks(1234567);

        Assert.Equal("2026-09-27T14:03:12.1234567Z", UtcTimestamp.ToText(value));
    }

    [Fact]
    public void Converts_offsets_to_utc()
    {
        var value = new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.FromHours(-4));

        Assert.Equal("2026-09-27T14:00:00.0000000Z", UtcTimestamp.ToText(value));
    }

    [Fact]
    public void Round_trips_to_the_tick()
    {
        var value = DateTimeOffset.UtcNow;

        Assert.Equal(value, UtcTimestamp.Parse(UtcTimestamp.ToText(value)));
        Assert.Equal(TimeSpan.Zero, UtcTimestamp.Parse(UtcTimestamp.ToText(value)).Offset);
    }

    [Fact]
    public void Unspecified_datetime_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => UtcTimestamp.ToText(new DateTime(2026, 1, 1)));
    }

    [Fact]
    public void Text_sorts_chronologically()
    {
        var earlier = UtcTimestamp.ToText(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var later = UtcTimestamp.ToText(new DateTimeOffset(2026, 11, 2, 3, 4, 5, TimeSpan.Zero));

        Assert.True(string.CompareOrdinal(earlier, later) < 0);
    }
}
