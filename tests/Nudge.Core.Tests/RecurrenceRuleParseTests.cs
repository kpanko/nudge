namespace Nudge.Core.Tests;

public class RecurrenceRuleParseTests
{
    [Theory]
    [InlineData("FREQ=DAILY")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=FR")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO,WE,FR;WKST=SU")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-1")]
    [InlineData("FREQ=MONTHLY;BYDAY=-1FR")]
    [InlineData("FREQ=MONTHLY;INTERVAL=6")]
    [InlineData("FREQ=YEARLY;BYMONTH=11;BYDAY=4TH")]
    [InlineData("FREQ=DAILY;UNTIL=20270131")]
    public void Parse_RoundTripsCanonicalText(string text)
    {
        Assert.Equal(text, RecurrenceRule.Parse(text).ToString());
    }

    [Fact]
    public void Parse_AcceptsPrefixLowerCaseAndDateTimeUntil()
    {
        var rule = RecurrenceRule.Parse(
            "rrule:freq=weekly;byday=fr;until=20270131T235959Z");

        Assert.Equal("FREQ=WEEKLY;BYDAY=FR;UNTIL=20270131", rule.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("INTERVAL=2")]
    [InlineData("FREQ=HOURLY")]
    [InlineData("FREQ=DAILY;FREQ=WEEKLY")]
    [InlineData("FREQ=DAILY;INTERVAL=0")]
    [InlineData("FREQ=DAILY;COUNT=5")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO;BYSETPOS=-1")]
    [InlineData("FREQ=WEEKLY;BYDAY=2FR")]
    [InlineData("FREQ=WEEKLY;BYDAY=XX")]
    [InlineData("FREQ=WEEKLY;BYMONTHDAY=15")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=0")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=32")]
    [InlineData("FREQ=MONTHLY;BYDAY=6MO")]
    [InlineData("FREQ=YEARLY;BYMONTH=13")]
    [InlineData("FREQ=YEARLY;BYDAY=20MO")]
    [InlineData("FREQ=YEARLY;BYMONTHDAY=15")]
    [InlineData("FREQ=DAILY;UNTIL=tomorrow")]
    [InlineData("FREQ=DAILY;UNTIL=20270231")]
    [InlineData("FREQ=DAILY;UNTIL=20270131T996199Z")]
    [InlineData("FREQ=DAILY;UNTIL=20270131X235959Z")]
    [InlineData("FREQ=YEARLY;INTERVAL=401")]
    [InlineData("FREQ=MONTHLY;INTERVAL=4801")]
    [InlineData("FREQ")]
    public void Parse_RejectsMalformedOrUnsupportedRules(string text)
    {
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse(text));
    }
}
