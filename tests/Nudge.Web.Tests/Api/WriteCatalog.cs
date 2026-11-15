using System.Text.Json.Nodes;

namespace Nudge.Web.Tests.Api;

/// <summary>
/// One request for every write endpoint the app has, each of which
/// changes something. <see cref="WriteGuaranteeTests"/> fails if the
/// app has a write endpoint that is not listed here, so a new one
/// cannot skip the guarantees every write must give.
/// </summary>
internal static class WriteCatalog
{
    public static readonly IReadOnlyList<WriteCase> Cases =
    [
        new(
            "POST",
            "/api/items",
            async api =>
            {
                var context = await api.CreateContextAsync("phone");
                var project = await api.CreateProjectAsync("House");
                return new Request(
                    "POST",
                    "/api/items",
                    $$"""
                    {
                      "title": "Order the worktop",
                      "notes": "Oak, three metres.",
                      "list": "nextAction",
                      "priority": "high",
                      "projectId": "{{project}}",
                      "deferUntil": "2026-11-18",
                      "due": { "date": "2026-11-20", "time": "14:30:00" },
                      "repeat": { "rule": "FREQ=WEEKLY;BYDAY=FR",
                                  "mode": "fixedSchedule" },
                      "contextIds": ["{{context}}"],
                      "reminders": [ { "offsetMinutes": -30 } ],
                      "doNotAutoReschedule": true
                    }
                    """);
            }),
        new(
            "PATCH",
            "/api/items/{id:guid}",
            async api => new Request(
                "PATCH",
                $"/api/items/{await WeeklyAsync(api)}",
                """{"title":"Water the ferns","list":"someday"}""")),
        new(
            "PUT",
            "/api/items/{id:guid}/due",
            async api => new Request(
                "PUT",
                $"/api/items/{await WeeklyAsync(api)}/due",
                """{"date":"2026-11-24"}""")),
        new(
            "DELETE",
            "/api/items/{id:guid}/due",
            async api =>
            {
                var item = await api.CreateItemAsync(
                    """{"title":"Post a card","due":{"date":"2026-11-20"}}""");
                return new Request(
                    "DELETE",
                    $"/api/items/{Id(item)}/due");
            }),
        new(
            "PUT",
            "/api/items/{id:guid}/repeat",
            async api => new Request(
                "PUT",
                $"/api/items/{await WeeklyAsync(api)}/repeat",
                """{"rule":"FREQ=MONTHLY","mode":"afterCompletion"}""")),
        new(
            "DELETE",
            "/api/items/{id:guid}/repeat",
            async api => new Request(
                "DELETE",
                $"/api/items/{await WeeklyAsync(api)}/repeat")),
        new(
            "POST",
            "/api/items/{id:guid}/complete",
            async api => new Request(
                "POST",
                $"/api/items/{await WeeklyAsync(api)}/complete",
                """{"occurrence":"2026-11-20"}""")),
        new(
            "DELETE",
            "/api/items/{id:guid}",
            async api => new Request(
                "DELETE",
                $"/api/items/{await WeeklyAsync(api)}")),
        new(
            "PUT",
            "/api/items/{id:guid}/contexts/{contextId:guid}",
            async api =>
            {
                var context = await api.CreateContextAsync("phone");
                return new Request(
                    "PUT",
                    $"/api/items/{await WeeklyAsync(api)}/contexts/{context}");
            }),
        new(
            "DELETE",
            "/api/items/{id:guid}/contexts/{contextId:guid}",
            async api =>
            {
                var context = await api.CreateContextAsync("phone");
                var item = await api.CreateItemAsync(
                    $$"""{"title":"Call the plumber","contextIds":["{{context}}"]}""");
                return new Request(
                    "DELETE",
                    $"/api/items/{Id(item)}/contexts/{context}");
            }),
        new(
            "POST",
            "/api/items/{id:guid}/reminders",
            async api => new Request(
                "POST",
                $"/api/items/{await WeeklyAsync(api)}/reminders",
                """{"offsetMinutes":540}""")),
        new(
            "DELETE",
            "/api/items/{id:guid}/reminders/{reminderId:guid}",
            async api =>
            {
                var item = await api.CreateWeeklyAsync();
                var reminder = item["reminders"]![0]!["id"]!;
                return new Request(
                    "DELETE",
                    $"/api/items/{Id(item)}/reminders/{reminder}");
            }),
        new(
            "POST",
            "/api/projects",
            async api => new Request(
                "POST",
                "/api/projects",
                $$"""{"name":"Kitchen","parentId":"{{await api.CreateProjectAsync("House")}}"}""")),
        new(
            "PATCH",
            "/api/projects/{id:guid}",
            async api =>
            {
                var house = await api.CreateProjectAsync("House");
                var kitchen = await api.CreateProjectAsync("Kitchen");
                return new Request(
                    "PATCH",
                    $"/api/projects/{kitchen}",
                    $$"""{"name":"New kitchen","parentId":"{{house}}"}""");
            }),
        new(
            "DELETE",
            "/api/projects/{id:guid}",
            async api => new Request(
                "DELETE",
                $"/api/projects/{await HouseWithContentsAsync(api)}")),
        new(
            "POST",
            "/api/contexts",
            _ => Task.FromResult(
                new Request(
                    "POST",
                    "/api/contexts",
                    """{"name":"errands"}"""))),
        new(
            "PATCH",
            "/api/contexts/{id:guid}",
            async api => new Request(
                "PATCH",
                $"/api/contexts/{await api.CreateContextAsync("phone")}",
                """{"name":"telephone"}""")),
        new(
            "DELETE",
            "/api/contexts/{id:guid}",
            async api =>
            {
                var context = await api.CreateContextAsync("phone");
                await api.CreateItemAsync(
                    $$"""{"title":"Call the plumber","contextIds":["{{context}}"]}""");
                return new Request("DELETE", $"/api/contexts/{context}");
            }),
        new(
            "POST",
            "/api/changes/{id:long}/undo",
            async api =>
            {
                var id = await WeeklyAsync(api);
                var reply = await api.SendAsync(
                    "PATCH",
                    $"/api/items/{id}",
                    """{"title":"Water the ferns"}""");
                return new Request(
                    "POST",
                    $"/api/changes/{reply.ChangeId}/undo");
            }),
    ];

    public static IEnumerable<object[]> Names =>
        Cases.Select(c => new object[] { c.Name });

    public static WriteCase Named(string name)
    {
        return Cases.Single(c => c.Name == name);
    }

    /// <summary>
    /// A project with a sub-project, an item in each, and an item in
    /// another project that deleting it must leave alone.
    /// </summary>
    public static async Task<string> HouseWithContentsAsync(Api api)
    {
        var house = await api.CreateProjectAsync("House");
        var kitchen = await api.CreateProjectAsync("Kitchen", house);
        var garden = await api.CreateProjectAsync("Garden");
        foreach (var (title, project) in new[]
        {
            ("Fix the gutter", house),
            ("Order the worktop", kitchen),
            ("Prune the roses", garden),
        })
        {
            await api.CreateItemAsync(
                $$"""{"title":"{{title}}","projectId":"{{project}}"}""");
        }

        return house;
    }

    private static async Task<string> WeeklyAsync(Api api)
    {
        return Id(await api.CreateWeeklyAsync());
    }

    private static string Id(JsonNode item)
    {
        return item["id"]!.GetValue<string>();
    }
}

/// <param name="Route">The route as the app maps it.</param>
/// <param name="Arrange">Makes what the request needs, through the
/// API, and returns the request.</param>
internal sealed record WriteCase(
    string Method,
    string Route,
    Func<Api, Task<Request>> Arrange)
{
    public string Name => $"{Method} {Route}";

    public override string ToString()
    {
        return Name;
    }
}
