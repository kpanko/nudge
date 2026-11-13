namespace Nudge.Core.Tests;

public class LocalTimeResolverTests
{
    private static readonly TimeZoneInfo NewYork =
        TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    [Fact]
    public void OrdinaryTime_KeepsWallClock()
    {
        var instant = LocalTimeResolver.ToInstant(
            new DateOnly(2026, 11, 12),
            new TimeOnly(9, 0),
            NewYork);

        Assert.Equal(
            new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.FromHours(-5)),
            instant);
        Assert.Equal(TimeSpan.FromHours(-5), instant.Offset);
    }

    [Fact]
    public void SkippedTime_MovesForwardByTheGap()
    {
        // Clocks jump from 02:00 to 03:00 on 14 March 2027.
        var instant = LocalTimeResolver.ToInstant(
            new DateOnly(2027, 3, 14),
            new TimeOnly(2, 30),
            NewYork);

        Assert.Equal(
            new DateTimeOffset(2027, 3, 14, 3, 30, 0, TimeSpan.FromHours(-4)),
            instant);
        Assert.Equal(TimeSpan.FromHours(-4), instant.Offset);
    }

    [Fact]
    public void RepeatedTime_ResolvesToFirstOccurrence()
    {
        // 01:30 happens twice on 7 November 2027.
        var instant = LocalTimeResolver.ToInstant(
            new DateOnly(2027, 11, 7),
            new TimeOnly(1, 30),
            NewYork);

        Assert.Equal(
            new DateTimeOffset(2027, 11, 7, 1, 30, 0, TimeSpan.FromHours(-4)),
            instant);
        Assert.Equal(TimeSpan.FromHours(-4), instant.Offset);
    }
}
