using System.Text.Json;
using Nudge.Core;
using Nudge.Web.Api;

namespace Nudge.Web.Components;

/// <summary>What a write from the web UI came to: the change it
/// recorded, if any, or why it was refused.</summary>
public sealed record WebWriteResult(
    long? ChangeId,
    string? Error)
{
    public static WebWriteResult Invalid(string error)
    {
        return new WebWriteResult(null, error);
    }
}

/// <summary>
/// The web UI's writes. Each one is the API's own work (the same
/// handler in <c>ItemEndpoints</c> and the others) carried out by
/// the same <see cref="WriteRunner"/> core, in a scope of its own as
/// an API request would be: one transaction, recorded in the change
/// log as made by <see cref="ChangeSource.WebUi"/>, undoable, and
/// kept against the form's idempotency key, so a form sent twice is
/// carried out once. The method and path kept with the key are the
/// API endpoint's that does the same.
/// </summary>
public sealed class WebWrites(
    IServiceScopeFactory scopes,
    IHttpContextAccessor accessor,
    ChangeSource source,
    ILogger<WebWrites> logger)
{
    /// <summary>What a form's key is: 32 hexadecimal digits, as a
    /// GUID without hyphens. It is kept as <c>web-</c> and the key.
    /// </summary>
    public static string NewKey()
    {
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>The idempotency key a form's key is kept under, or
    /// null if it is not one this UI makes.</summary>
    public static string? RecordKey(string? key)
    {
        return key is { Length: 32 } && key.All(Uri.IsHexDigit)
            ? $"web-{key}"
            : null;
    }

    /// <summary>
    /// What makes two sendings of a form with the same key the same
    /// request: the page it was sent to and every field as sent
    /// (the form's name, its values, where to go after), but not the
    /// antiforgery token. Taken from the request alone, so that a
    /// repeat can be recognised before the page is rendered; see
    /// <see cref="WebUi.UseUiReplay"/>.
    /// </summary>
    public static string FormFingerprint(
        PathString page,
        IFormCollection form)
    {
        var fields = form
            .Where(field => field.Key is not (AntiforgeryField or "key"))
            .OrderBy(field => field.Key, StringComparer.Ordinal)
            .Select(field => new[] { field.Key }.Concat(field.Value!))
            .ToList();
        return WriteRunner.Fingerprint(
            "POST",
            page.Value ?? "/",
            JsonSerializer.SerializeToUtf8Bytes(fields));
    }

    /// <summary>Where the browser goes after a write: a path on this
    /// site (anything else is the home page), with the change to
    /// offer to undo.</summary>
    public static string Target(
        string? then,
        long? changeId)
    {
        var path = Nudge.Web.Auth.OwnerAuthentication
            .LocalReturnUrl(then)
            .Split('?', '#')[0];
        return changeId is { } id ? $"{path}?change={id}" : path;
    }

    private const string AntiforgeryField = "__RequestVerificationToken";

    public Task<WebWriteResult> CaptureAsync(
        string key,
        string? title)
    {
        return RunAsync<ItemCreate, ItemView>(
            key,
            "POST",
            "/api/items",
            "Create item",
            new ItemCreate { Title = title ?? "" },
            ["title"],
            [],
            ItemEndpoints.CreateAsync);
    }

    /// <summary>Edits an item; only the properties named in
    /// <paramref name="present"/> (in JSON) are changed.</summary>
    public Task<WebWriteResult> EditAsync(
        string key,
        Guid id,
        ItemPatch patch,
        IReadOnlySet<string> present)
    {
        return RunAsync<ItemPatch, ItemView>(
            key,
            "PATCH",
            $"/api/items/{id}",
            "Edit item",
            patch,
            present,
            Route(("id", id)),
            ItemEndpoints.EditAsync);
    }

    public Task<WebWriteResult> DeferAsync(
        string key,
        Guid id,
        DateOnly? until)
    {
        return EditAsync(
            key,
            id,
            new ItemPatch { DeferUntil = until },
            new HashSet<string> { "deferUntil" });
    }

    public Task<WebWriteResult> RescheduleAsync(
        string key,
        Guid id,
        DateOnly date,
        TimeOnly? time)
    {
        return RunAsync<DueInput, ItemView>(
            key,
            "PUT",
            $"/api/items/{id}/due",
            "Reschedule item",
            new DueInput { Date = date, Time = time },
            time is null ? ["date"] : ["date", "time"],
            Route(("id", id)),
            ItemEndpoints.RescheduleAsync);
    }

    public Task<WebWriteResult> ClearDueAsync(
        string key,
        Guid id)
    {
        return NoBodyAsync(
            key,
            "DELETE",
            $"/api/items/{id}/due",
            "Clear due date",
            Route(("id", id)),
            ItemEndpoints.ClearDueAsync);
    }

    public Task<WebWriteResult> RepeatAsync(
        string key,
        Guid id,
        string? rule,
        RecurrenceMode mode,
        DateOnly? start)
    {
        return RunAsync<RepeatInput, ItemView>(
            key,
            "PUT",
            $"/api/items/{id}/repeat",
            "Set repeat rule",
            new RepeatInput { Rule = rule ?? "", Mode = mode, Start = start },
            start is null ? ["rule", "mode"] : ["rule", "mode", "start"],
            Route(("id", id)),
            ItemEndpoints.RepeatAsync);
    }

    public Task<WebWriteResult> StopRepeatingAsync(
        string key,
        Guid id)
    {
        return NoBodyAsync(
            key,
            "DELETE",
            $"/api/items/{id}/repeat",
            "Stop repeating",
            Route(("id", id)),
            ItemEndpoints.StopRepeatingAsync);
    }

    /// <param name="occurrence">The occurrence the page showed, for
    /// a repeating item; a stale one is refused.</param>
    public Task<WebWriteResult> CompleteAsync(
        string key,
        Guid id,
        DateOnly? occurrence)
    {
        return RunAsync<CompleteRequest, ItemView>(
            key,
            "POST",
            $"/api/items/{id}/complete",
            "Complete item",
            new CompleteRequest { Occurrence = occurrence },
            occurrence is null ? [] : ["occurrence"],
            Route(("id", id)),
            ItemEndpoints.CompleteAsync);
    }

    public Task<WebWriteResult> DeleteAsync(
        string key,
        Guid id)
    {
        return NoBodyAsync(
            key,
            "DELETE",
            $"/api/items/{id}",
            "Delete item",
            Route(("id", id)),
            ItemEndpoints.DeleteAsync);
    }

    public Task<WebWriteResult> AddContextAsync(
        string key,
        Guid id,
        Guid contextId)
    {
        return NoBodyAsync(
            key,
            "PUT",
            $"/api/items/{id}/contexts/{contextId}",
            "Add context to item",
            Route(("id", id), ("contextId", contextId)),
            ItemEndpoints.AddContextAsync);
    }

    public Task<WebWriteResult> RemoveContextAsync(
        string key,
        Guid id,
        Guid contextId)
    {
        return NoBodyAsync(
            key,
            "DELETE",
            $"/api/items/{id}/contexts/{contextId}",
            "Remove context from item",
            Route(("id", id), ("contextId", contextId)),
            ItemEndpoints.RemoveContextAsync);
    }

    /// <summary>Adds a reminder some minutes from the due date
    /// (negative for before it).</summary>
    public Task<WebWriteResult> AddReminderAsync(
        string key,
        Guid id,
        int offsetMinutes)
    {
        return RunAsync<ReminderInput, ItemView>(
            key,
            "POST",
            $"/api/items/{id}/reminders",
            "Add reminder",
            new ReminderInput { OffsetMinutes = offsetMinutes },
            ["offsetMinutes"],
            Route(("id", id)),
            ItemEndpoints.AddReminderAsync);
    }

    public Task<WebWriteResult> RemoveReminderAsync(
        string key,
        Guid id,
        Guid reminderId)
    {
        return NoBodyAsync(
            key,
            "DELETE",
            $"/api/items/{id}/reminders/{reminderId}",
            "Remove reminder",
            Route(("id", id), ("reminderId", reminderId)),
            ItemEndpoints.RemoveReminderAsync);
    }

    public Task<WebWriteResult> CreateProjectAsync(
        string key,
        string? name,
        Guid? parentId)
    {
        return RunAsync<ProjectCreate, ProjectView>(
            key,
            "POST",
            "/api/projects",
            "Create project",
            new ProjectCreate { Name = name ?? "", ParentId = parentId },
            parentId is null ? ["name"] : ["name", "parentId"],
            [],
            ProjectEndpoints.CreateAsync);
    }

    public Task<WebWriteResult> CreateContextAsync(
        string key,
        string? name)
    {
        return RunAsync<ContextCreate, ContextView>(
            key,
            "POST",
            "/api/contexts",
            "Create context",
            new ContextCreate { Name = name ?? "" },
            ["name"],
            [],
            ContextEndpoints.CreateAsync);
    }

    public Task<WebWriteResult> UndoAsync(
        string key,
        long changeId)
    {
        return RunAsync<NoBody, ChangeView>(
            key,
            "POST",
            $"/api/changes/{changeId}/undo",
            "Undo change",
            NoBody.Value,
            [],
            new RouteValueDictionary
            {
                ["id"] = changeId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            },
            ChangeEndpoints.UndoAsync);
    }

    private static RouteValueDictionary Route(
        params (string Name, Guid Value)[] values)
    {
        var route = new RouteValueDictionary();
        foreach (var (name, value) in values)
        {
            route[name] = value.ToString();
        }

        return route;
    }

    private Task<WebWriteResult> NoBodyAsync<TData>(
        string key,
        string method,
        string path,
        string description,
        RouteValueDictionary route,
        Func<Write<NoBody>, Task<WriteOutcome<TData>>> work)
    {
        return RunAsync(
            key,
            method,
            path,
            description,
            NoBody.Value,
            [],
            route,
            work);
    }

    private async Task<WebWriteResult> RunAsync<TBody, TData>(
        string key,
        string method,
        string path,
        string description,
        TBody body,
        IReadOnlyCollection<string> named,
        RouteValueDictionary route,
        Func<Write<TBody>, Task<WriteOutcome<TData>>> work)
    {
        var present = named.ToHashSet(StringComparer.Ordinal);
        var http = accessor.HttpContext
            ?? throw new InvalidOperationException(
                "A write from the web UI was made outside a request.");
        if (RecordKey(key) is not { } recordKey
            || !http.Request.HasFormContentType)
        {
            return WebWriteResult.Invalid(
                "The form is out of date. Reload the page and try again.");
        }

        var fingerprint = FormFingerprint(
            http.Request.Path,
            http.Request.Form);
        await using var scope = scopes.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<WriteRunner>();
        try
        {
            var done = await runner.ExecuteAsync(
                new WriteRequest<TBody>(
                    source.For(http),
                    description,
                    body,
                    present,
                    route,
                    DryRun: false,
                    recordKey,
                    method,
                    path,
                    fingerprint),
                work,
                http.RequestAborted);
            return new WebWriteResult(done.ChangeId, null);
        }
        catch (ApiProblem problem)
        {
            if (problem.Cause is not null)
            {
                logger.LogWarning(
                    problem.Cause,
                    "{Description} from the web UI was refused: {Problem}",
                    description,
                    problem.Detail ?? problem.Title);
            }

            return WebWriteResult.Invalid(Message(problem));
        }
    }

    /// <summary>A refusal in words for the page: what was wrong with
    /// each field, or the detail, or the title.</summary>
    private static string Message(ApiProblem problem)
    {
        if (problem.Status == StatusCodes.Status422UnprocessableEntity)
        {
            // The form's key was used before with other values: the
            // page was sent, changed and sent again.
            return "This form was already sent with other values."
                + " Reload the page and try again.";
        }

        if (problem.Errors is { Count: > 0 } errors)
        {
            return string.Join(
                " ",
                errors.SelectMany(
                    error => error.Value.Select(
                        message => $"{Field(error.Key)}: {message}")));
        }

        return problem.Detail ?? problem.Title;
    }

    private static string Field(string name)
    {
        return name.Length == 0
            ? name
            : char.ToUpperInvariant(name[0]) + name[1..];
    }
}
