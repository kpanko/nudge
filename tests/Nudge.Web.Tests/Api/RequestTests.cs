using System.Net;

namespace Nudge.Web.Tests.Api;

/// <summary>
/// How write requests are read and how idempotency keys behave,
/// beyond what <see cref="WriteGuaranteeTests"/> checks on every
/// endpoint.
/// </summary>
public sealed class RequestTests : IDisposable
{
    private const string Plumber = """{"title":"Call the plumber"}""";

    private readonly Api _api = new();

    public void Dispose()
    {
        _api.Dispose();
    }

    [Theory]
    [InlineData("""{"title":"Call","Title":"Call"}""", "Title")]
    [InlineData("""{"title":"Call","dueDate":"2026-11-20"}""", "dueDate")]
    [InlineData("""{"title":"Call","due":{"date":"2026-11-20","tz":"x"}}""", "due.tz")]
    [InlineData("""{"title":"Call","title":"Again"}""", "title")]
    [InlineData("""{"title":42}""", "title")]
    [InlineData("""{"title":"Call","doNotAutoReschedule":"true"}""", "doNotAutoReschedule")]
    [InlineData("""{"title":"Call","due":{"date":"20/11/2026"}}""", "due.date")]
    [InlineData("""{"title":"Call",}""", "body")]
    [InlineData("""{"title":"Call""", "body")]
    [InlineData("""["Call"]""", "body")]
    [InlineData("null", "body")]
    public async Task Body_ThatIsNotUnderstood_IsRefused_NamingWhatIsWrong(
        string body,
        string field)
    {
        var reply = await _api.SendAsync("POST", "/api/items", body);

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Equal("application/problem+json", reply.ContentType);
        Assert.NotEmpty(reply.Error(field));
        Assert.Equal(0, _api.Count("Changes"));
    }

    [Fact]
    public async Task Body_WhereNoneIsTaken_IsRefused()
    {
        var item = await _api.CreateItemAsync(Plumber);

        var reply = await _api.SendAsync(
            "DELETE",
            $"/api/items/{item["id"]}",
            "{}");

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.NotEmpty(reply.Error("body"));
    }

    [Fact]
    public async Task Body_ThatIsRequired_IsRequired()
    {
        var reply = await _api.SendAsync("POST", "/api/items");

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.NotEmpty(reply.Error("body"));
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/json; charset=latin1")]
    public async Task Body_ThatIsNotJson_IsRefused(string contentType)
    {
        var reply = await _api.SendAsync(
            "POST",
            "/api/items",
            Plumber,
            contentType: contentType);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, reply.Status);
    }

    [Fact]
    public async Task Body_ThatIsTooLarge_IsRefused()
    {
        var notes = new string('x', 1 << 20);

        var reply = await _api.SendAsync(
            "POST",
            "/api/items",
            $$"""{"title":"Call","notes":"{{notes}}"}""");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, reply.Status);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("café")]
    public async Task Key_ThatIsNotValid_IsRefused(string key)
    {
        var reply = await _api.SendAsync(
            "POST",
            "/api/items",
            Plumber,
            key);

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Equal(0, _api.Count("Changes"));
    }

    [Fact]
    public async Task Key_ThatIsTooLong_IsRefused()
    {
        var reply = await _api.SendAsync(
            "POST",
            "/api/items",
            Plumber,
            new string('k', 256));

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
    }

    // A dry run with a key leaves no record of it, so the real
    // request with the same key is carried out; and as the dry run
    // and the real request count as the same request, a retry of the
    // real one is answered from the record.
    [Fact]
    public async Task Key_DryRunThenRealThenRetry()
    {
        var dry = await _api.SendAsync(
            "POST",
            "/api/items?dryRun=true",
            Plumber,
            "key-1");
        Assert.True(dry.Json["dryRun"]!.GetValue<bool>());
        Assert.Equal(0, _api.Count("IdempotencyRecords"));
        Assert.Equal(0, _api.Count("Changes"));

        var real = await _api.SendAsync("POST", "/api/items", Plumber, "key-1");
        Assert.Equal(HttpStatusCode.Created, real.Status);
        Assert.False(real.Replayed);
        Assert.Equal(1, _api.Count("IdempotencyRecords"));
        Assert.Equal(1, _api.Count("Changes"));

        var retry = await _api.SendAsync("POST", "/api/items", Plumber, "key-1");
        Assert.True(retry.Replayed);
        Assert.Equal(HttpStatusCode.Created, retry.Status);
        Assert.Equal(real.Body, retry.Body);

        // A dry run of it now says what happened: it was done.
        var dryAgain = await _api.SendAsync(
            "POST",
            "/api/items?dryRun=true",
            Plumber,
            "key-1");
        Assert.True(dryAgain.Replayed);
        Assert.Equal(real.Body, dryAgain.Body);
        Assert.Equal(1, _api.Count("Changes"));
        Assert.Single((await _api.GetAsync("/api/items")).AsArray());
    }

    // A dry run with a key the real request already used is that
    // request again: it gets the kept answer, with the real
    // request's status and "dryRun": false, and does nothing.
    [Fact]
    public async Task Key_UsedByTheRealRequest_ThenDryRun_GetsTheKeptAnswer()
    {
        var real = await _api.SendAsync("POST", "/api/items", Plumber, "key-1");
        Assert.Equal(HttpStatusCode.Created, real.Status);
        var after = _api.Everything();

        var dry = await _api.SendAsync(
            "POST",
            "/api/items?dryRun=true",
            Plumber,
            "key-1");

        Assert.Equal(HttpStatusCode.Created, dry.Status);
        Assert.True(dry.Replayed);
        Assert.False(dry.Json["dryRun"]!.GetValue<bool>());
        Assert.Equal(real.Body, dry.Body);
        Assert.Equal(after, _api.Everything());
    }

    // A failed request changes nothing and leaves no record, so the
    // client can put it right and send it again with the same key.
    [Fact]
    public async Task Key_OfAFailedRequest_IsNotKept()
    {
        var failed = await _api.SendAsync(
            "POST",
            "/api/items",
            """{"title":"   "}""",
            "key-1");
        Assert.Equal(HttpStatusCode.BadRequest, failed.Status);
        Assert.Equal(0, _api.Count("IdempotencyRecords"));

        var fixedUp = await _api.SendAsync(
            "POST",
            "/api/items",
            Plumber,
            "key-1");
        Assert.Equal(HttpStatusCode.Created, fixedUp.Status);
    }

    [Fact]
    public async Task Key_RetriedAfterItsChangeWasUndone_IsNotDoneAgain()
    {
        var created = await _api.SendAsync(
            "POST",
            "/api/items",
            Plumber,
            "key-1");
        await _api.WriteAsync(
            "POST",
            $"/api/changes/{created.ChangeId}/undo");

        var retry = await _api.SendAsync("POST", "/api/items", Plumber, "key-1");

        Assert.True(retry.Replayed);
        Assert.Empty((await _api.GetAsync("/api/items")).AsArray());
        Assert.Equal(2, _api.Count("Changes"));
    }

    [Fact]
    public async Task Key_SentWithADifferentBody_IsRefused()
    {
        await _api.SendAsync("POST", "/api/items", Plumber, "key-1");

        var other = await _api.SendAsync(
            "POST",
            "/api/items",
            """{"title":"Call the electrician"}""",
            "key-1");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, other.Status);
        Assert.Single((await _api.GetAsync("/api/items")).AsArray());
    }

    [Fact]
    public async Task Key_IsCaseSensitive()
    {
        await _api.SendAsync("POST", "/api/items", Plumber, "key-1");

        var other = await _api.SendAsync("POST", "/api/items", Plumber, "KEY-1");

        Assert.False(other.Replayed);
        Assert.Equal(2, (await _api.GetAsync("/api/items")).AsArray().Count);
    }

    // Two copies of a request at the same moment: the second waits
    // for the first and is answered from its record.
    [Fact]
    public async Task Key_SentTwiceAtOnce_IsCarriedOutOnce()
    {
        var item = await _api.CreateWeeklyAsync();
        var complete = new Request(
            "POST",
            $"/api/items/{item["id"]}/complete",
            """{"occurrence":"2026-11-20"}""");
        var changes = _api.Count("Changes");

        var replies = await Task.WhenAll(
            Enumerable.Range(0, 8)
                .Select(_ => _api.SendAsync(complete, "key-1")));

        Assert.All(replies, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        Assert.Single(replies, r => !r.Replayed);
        Assert.Single(replies.Select(r => r.Body).Distinct());
        Assert.Equal(changes + 1, _api.Count("Changes"));
    }

    // Without a key, the occurrence check stops the duplicates.
    [Fact]
    public async Task Complete_SentManyTimesAtOnceWithoutAKey_AdvancesOnce()
    {
        var item = await _api.CreateWeeklyAsync();
        var complete = new Request(
            "POST",
            $"/api/items/{item["id"]}/complete",
            """{"occurrence":"2026-11-20"}""");

        var replies = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => _api.SendAsync(complete)));

        Assert.Single(replies, r => r.Status == HttpStatusCode.OK);
        Assert.All(
            replies.Where(r => r.Status != HttpStatusCode.OK),
            r => Assert.Equal(HttpStatusCode.Conflict, r.Status));
        var now = await _api.GetAsync($"/api/items/{item["id"]}");
        Assert.Equal("2026-11-27", now["due"]!["date"]!.GetValue<string>());
    }

    [Fact]
    public async Task UnknownRoute_IsAProblem()
    {
        var reply = await _api.SendAsync("GET", "/api/nothing-here");

        Assert.Equal(HttpStatusCode.NotFound, reply.Status);
        Assert.Equal("application/problem+json", reply.ContentType);
    }
}
