using System.Net;
using System.Text.Json.Nodes;

namespace Nudge.Web.Tests.Api;

/// <summary>
/// The M1 acceptance criteria, through HTTP. The fourth, "every write
/// is in the change log and can be undone", is proven for every
/// write endpoint in <see cref="WriteGuaranteeTests"/>.
/// </summary>
public sealed class AcceptanceTests : IDisposable
{
    private readonly Api _api = new();

    public void Dispose()
    {
        _api.Dispose();
    }

    private static string Id(JsonNode item)
    {
        return item["id"]!.GetValue<string>();
    }

    private Task<JsonNode> GetItemAsync(string id)
    {
        return _api.GetAsync($"/api/items/{id}");
    }

    // Named regression test (M1): completing once advances once.
    [Fact]
    public async Task CompletingARepeatingItemOnce_AdvancesItExactlyOnce()
    {
        var id = Id(await _api.CreateWeeklyAsync());
        var complete = new Request(
            "POST",
            $"/api/items/{id}/complete",
            """{"occurrence":"2026-11-20"}""");

        var first = await _api.SendAsync(complete, "complete-20-nov");

        Assert.Equal(HttpStatusCode.OK, first.Status);
        var item = first.Json["data"]!;
        Assert.Equal("2026-11-27", item["due"]!["date"]!.GetValue<string>());
        Assert.Equal(
            "2026-11-27",
            item["repeat"]!["occurrence"]!.GetValue<string>());
        Assert.Null(item["completedAt"]);

        // The same request again, with its key: answered, not redone.
        var retry = await _api.SendAsync(complete, "complete-20-nov");
        Assert.True(retry.Replayed);
        Assert.Equal(first.Body, retry.Body);

        // The same request again without a key names an occurrence
        // that is no longer current, and is refused.
        var again = await _api.SendAsync(complete);
        Assert.Equal(HttpStatusCode.Conflict, again.Status);
        Assert.Equal(
            "2026-11-27",
            again.Json["currentOccurrence"]!.GetValue<string>());

        var now = await GetItemAsync(id);
        Assert.Equal("2026-11-27", now["due"]!["date"]!.GetValue<string>());
        Assert.Equal(2, _api.Count("Changes"));
    }

    [Fact]
    public async Task CompletingAnItemThatDoesNotRepeat_Twice_IsRefused()
    {
        var id = Id(await _api.CreateItemAsync("""{"title":"Post a card"}"""));
        var complete = new Request("POST", $"/api/items/{id}/complete");

        Assert.Equal(HttpStatusCode.OK, (await _api.SendAsync(complete)).Status);
        var again = await _api.SendAsync(complete);

        Assert.Equal(HttpStatusCode.Conflict, again.Status);
        Assert.Contains("already completed", again.Body);
        Assert.Equal(2, _api.Count("Changes"));
    }

    // Named regression test (M1): setting a due date keeps the rule.
    // All three ways in: PUT due keeps it, DELETE due on a repeating
    // item is refused, and PATCH cannot touch the due date or rule.
    [Fact]
    public async Task SettingADueDate_NeverRemovesTheRepeatRule()
    {
        var created = await _api.CreateWeeklyAsync();
        var id = Id(created);
        var repeat = created["repeat"]!.ToJsonString();

        var moved = await _api.WriteAsync(
            "PUT",
            $"/api/items/{id}/due",
            """{"date":"2026-11-23","time":"09:00:00"}""");
        Assert.Equal("2026-11-23", moved["due"]!["date"]!.GetValue<string>());
        Assert.Equal(repeat, moved["repeat"]!.ToJsonString());

        var before = _api.Everything();
        var cleared = await _api.SendAsync("DELETE", $"/api/items/{id}/due");
        Assert.Equal(HttpStatusCode.Conflict, cleared.Status);
        Assert.Contains("must have a due date", cleared.Body);
        Assert.Equal(before, _api.Everything());

        foreach (var property in new[]
        {
            """{"due":{"date":"2026-12-01"}}""",
            """{"dueDate":"2026-12-01"}""",
            """{"repeat":null}""",
            """{"rule":"FREQ=DAILY"}""",
            """{"title":"Water the ferns","repeat":null}""",
        })
        {
            var patched = await _api.SendAsync(
                "PATCH",
                $"/api/items/{id}",
                property);
            Assert.Equal(HttpStatusCode.BadRequest, patched.Status);
            Assert.Contains("/api/items/{id}/", patched.Body);
        }

        Assert.Equal(before, _api.Everything());
        var now = await GetItemAsync(id);
        Assert.Equal(repeat, now["repeat"]!.ToJsonString());
        Assert.Equal("Water the plants", now["title"]!.GetValue<string>());
    }

    // Rescheduling one occurrence leaves the rule and the reminders
    // exactly as they were, and the next occurrence still follows
    // the rule, not the day the occurrence was moved to.
    [Fact]
    public async Task ReschedulingOneOccurrence_LeavesRuleAndRemindersIntact()
    {
        var created = await _api.CreateWeeklyAsync();
        var id = Id(created);
        var repeat = created["repeat"]!.ToJsonString();
        var reminders = created["reminders"]!.ToJsonString();
        Assert.Equal(2, created["reminders"]!.AsArray().Count);

        // Friday 20th moved to Tuesday 24th.
        var moved = await _api.WriteAsync(
            "PUT",
            $"/api/items/{id}/due",
            """{"date":"2026-11-24","time":"14:30:00"}""");

        Assert.Equal("2026-11-24", moved["due"]!["date"]!.GetValue<string>());
        Assert.Equal(repeat, moved["repeat"]!.ToJsonString());
        Assert.Equal(reminders, moved["reminders"]!.ToJsonString());
        Assert.Equal(
            "2026-11-20",
            moved["repeat"]!["occurrence"]!.GetValue<string>());

        // Done on the Tuesday it was moved to. A week on from the
        // moved date would be Tuesday 1 December; the rule says
        // Friday 27 November.
        _api.Clock.Now = new DateTimeOffset(
            2026,
            11,
            24,
            20,
            0,
            0,
            TimeSpan.Zero);
        var done = await _api.WriteAsync(
            "POST",
            $"/api/items/{id}/complete",
            """{"occurrence":"2026-11-20"}""");

        Assert.Equal("2026-11-27", done["due"]!["date"]!.GetValue<string>());
        Assert.Equal("14:30:00", done["due"]!["time"]!.GetValue<string>());
        Assert.Equal(
            "2026-11-27",
            done["repeat"]!["occurrence"]!.GetValue<string>());
        foreach (var part in new[] { "rule", "mode", "start" })
        {
            Assert.Equal(
                created["repeat"]![part]!.ToJsonString(),
                done["repeat"]![part]!.ToJsonString());
        }

        Assert.Equal(reminders, done["reminders"]!.ToJsonString());
    }

    // A known limit of the occurrence check. After completion mode
    // counts from the day it was done, so completing one interval
    // early gives the same occurrence back, and a second completion
    // sent without a key passes the check. Nothing is lost: the item
    // ends up exactly as after one completion, and the second
    // records no change. The idempotency key is the full protection.
    [Fact]
    public async Task AfterCompletion_DuplicateWithoutAKey_LeavesTheSameSchedule()
    {
        var created = await _api.CreateItemAsync(
            """
            {
              "title": "Change the water filter",
              "due": { "date": "2026-11-20" },
              "repeat": { "rule": "FREQ=WEEKLY",
                          "mode": "afterCompletion" }
            }
            """);
        var id = Id(created);
        var complete = new Request(
            "POST",
            $"/api/items/{id}/complete",
            """{"occurrence":"2026-11-20"}""");

        // Friday 13th: exactly one week early.
        var first = await _api.SendAsync(complete);
        var second = await _api.SendAsync(complete);

        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(
            created.ToJsonString(),
            first.Json["data"]!.ToJsonString());
        Assert.Equal(
            first.Json["data"]!.ToJsonString(),
            second.Json["data"]!.ToJsonString());
        Assert.Null(second.Json["change"]);
    }
}
