using FluentAssertions;
using Roma.Domain.Common;

namespace Roma.Tests.Common;

public class TimeUtilsTests
{
    [Theory]
    [InlineData(0, "0m")]
    [InlineData(59, "0m")]
    [InlineData(60, "1m")]
    [InlineData(3599, "59m")]
    [InlineData(3600, "1h")]
    [InlineData(3660, "1h 1m")]
    [InlineData(7200, "2h")]
    [InlineData(7325, "2h 2m")]
    public void FormatDuration_FormatsExpectedStrings(int seconds, string expected)
    {
        var result = TimeUtils.FormatDuration(seconds);
        result.Should().Be(expected);
    }

    [Fact]
    public void ParseOffsets_ValidOffsets_ReturnsSortedMinutes()
    {
        var spec = "-7d,-3d,-1d,-2h,0";
        var result = TimeUtils.ParseOffsets(spec);

        result.Should().Equal(-10080, -4320, -1440, -120, 0);
    }

    [Fact]
    public void ParseOffsets_DuplicateOffsets_ThrowsArgumentException()
    {
        var spec = "-1d,-1440m";
        var act = () => TimeUtils.ParseOffsets(spec);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Duplicate offset*");
    }

    [Fact]
    public void ParseOffsets_MalformedOffset_ThrowsArgumentException()
    {
        var spec = "-1week";
        var act = () => TimeUtils.ParseOffsets(spec);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Invalid offset format*");
    }

    [Fact]
    public void CalcDueAt_CalculatesCorrectMoment()
    {
        var moment = new LocalParts(2026, 10, 15, 14, 30);
        var due = TimeUtils.CalcDueAt(moment, -120); // 2 hours before

        due.Should().Be("2026-10-15T12:30");
    }

    [Fact]
    public void DayTimeToUtc_ConvertsToUtcAccordingToTz()
    {
        // Using UTC as simple reference test
        var utc = TimeUtils.DayTimeToUtc("2026-10-15", "14:30", "UTC");
        utc.Year.Should().Be(2026);
        utc.Month.Should().Be(10);
        utc.Day.Should().Be(15);
        utc.Hour.Should().Be(14);
        utc.Minute.Should().Be(30);
        utc.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ResolveDateRange_WithValidMonth_ReturnsFirstAndLastDay()
    {
        var (from, to) = TimeUtils.ResolveDateRange("2026-02", null, null, "UTC");
        from.Should().Be("2026-02-01");
        to.Should().Be("2026-02-28");
    }

    [Fact]
    public void ResolveDateRange_WithExplicitFromTo_ReturnsRange()
    {
        var (from, to) = TimeUtils.ResolveDateRange(null, "2026-10-05", "2026-10-20", "UTC");
        from.Should().Be("2026-10-05");
        to.Should().Be("2026-10-20");
    }

    [Fact]
    public void ResolveDateRange_WhenFromAfterTo_ThrowsArgumentException()
    {
        var act = () => TimeUtils.ResolveDateRange(null, "2026-10-20", "2026-10-05", "UTC");
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be after*");
    }

    [Fact]
    public void ResolveDateRange_WithInvalidMonth_ThrowsArgumentException()
    {
        var act = () => TimeUtils.ResolveDateRange("invalid-month", null, null, "UTC");
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Invalid month format*");
    }
}
