using System.Net;

namespace Nudge.Web.Tests.Api;

/// <summary>
/// Completing an item decides what day it is in the owner's time
/// zone, not in UTC. The clock here is at 02:30 UTC on Saturday 14
/// November 2026, which is still Friday 13th, 21:30, in New York.
/// Completing is the only thing in the API that turns the time into
/// a day: deleting and finishing record the instant itself.
/// </summary>
public sealed class OwnerDayTests : IDisposable
{
    private readonly Api _api = new();

    public OwnerDayTests()
    {
        _api.Clock.Now = new DateTimeOffset(
            2026,
            11,
            14,
            2,
            30,
            0,
            TimeSpan.Zero);
    }

    public void Dispose()
    {
        _api.Dispose();
    }

    private async Task<string> CompleteAsync(
        string rule,
        string mode)
    {
        var item = await _api.CreateItemAsync(
            $$"""
            {
              "title": "Change the water filter",
              "due": { "date": "2026-11-13" },
              "repeat": { "rule": "{{rule}}", "mode": "{{mode}}" }
            }
            """);
        var reply = await _api.SendAsync(
            "POST",
            $"/api/items/{item["id"]}/complete",
            """{"occurrence":"2026-11-13"}""");
        Assert.Equal(HttpStatusCode.OK, reply.Status);
        return reply.Json["data"]!["due"]!["date"]!.GetValue<string>();
    }

    // Counted from Friday 13th, the owner's day; from the UTC date
    // it would be Saturday 21st.
    [Fact]
    public async Task AfterCompletion_CountsFromTheOwnersDay()
    {
        Assert.Equal(
            "2026-11-20",
            await CompleteAsync("FREQ=WEEKLY", "afterCompletion"));
    }

    // Done on the day it was due, in the owner's zone, so the next is
    // the next day; by the UTC date it would be late, and skip to the
    // 15th.
    [Fact]
    public async Task FixedSchedule_LateOrNot_IsJudgedOnTheOwnersDay()
    {
        Assert.Equal(
            "2026-11-14",
            await CompleteAsync("FREQ=DAILY", "fixedSchedule"));
    }

    // Finishing an item records the instant, which is the same in
    // every zone.
    [Fact]
    public async Task Finishing_RecordsTheInstant()
    {
        var item = await _api.CreateItemAsync("""{"title":"Post a card"}""");

        var done = await _api.WriteAsync(
            "POST",
            $"/api/items/{item["id"]}/complete");

        Assert.Equal(
            "2026-11-14T02:30:00Z",
            done["completedAt"]!.GetValue<string>());
    }
}
