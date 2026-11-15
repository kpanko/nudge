using Microsoft.EntityFrameworkCore;
using Nudge.Core;
using Nudge.Web.Data;

namespace Nudge.Web.Api;

/// <summary>
/// Items. The due date, the repeat rule, completion, contexts and
/// reminders each have an endpoint of their own, so that changing
/// one can never change another: rescheduling cannot touch the rule,
/// and editing an item cannot touch either.
/// </summary>
internal static class ItemEndpoints
{
    private const string One = "/items/{id:guid}";

    /// <summary>Where to go for what PATCH does not take.</summary>
    private static readonly IReadOnlyDictionary<string, string> PatchHints =
        Hints(
            ("due", "Reschedule with PUT /api/items/{id}/due, or clear the"
                + " due date with DELETE /api/items/{id}/due."),
            ("dueDate", "Reschedule with PUT /api/items/{id}/due."),
            ("dueTime", "Reschedule with PUT /api/items/{id}/due."),
            ("repeat", "Change how an item repeats with PUT"
                + " /api/items/{id}/repeat, or stop it with DELETE"
                + " /api/items/{id}/repeat."),
            ("rule", "Change the rule with PUT /api/items/{id}/repeat."),
            ("recurrence", "Change the rule with PUT"
                + " /api/items/{id}/repeat."),
            ("occurrence", "The occurrence moves only by completing it"
                + " (POST /api/items/{id}/complete); reschedule it with"
                + " PUT /api/items/{id}/due."),
            ("contextIds", "Add or remove one context with PUT or DELETE"
                + " /api/items/{id}/contexts/{contextId}."),
            ("reminders", "Add a reminder with POST"
                + " /api/items/{id}/reminders, remove one with DELETE"
                + " /api/items/{id}/reminders/{reminderId}."),
            ("completedAt", "Complete with POST /api/items/{id}/complete;"
                + " reopen by undoing that change."),
            ("id", "An item's id cannot change."));

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/items", ListAsync)
            .WithName("ListItems")
            .Produces<List<ItemView>>()
            .WithSummary("List items")
            .WithDescription(
                "Open items, unless includeCompleted=true. Filters:"
                + " list, projectId, contextId.")
            .Takes(
                new QueryParameter(
                    "list",
                    typeof(ItemState),
                    "Only items on this GTD list."),
                new QueryParameter(
                    "projectId",
                    typeof(Guid),
                    "Only items in this project; not those in the"
                    + " projects under it."),
                new QueryParameter(
                    "contextId",
                    typeof(Guid),
                    "Only items with this context."),
                new QueryParameter(
                    "includeCompleted",
                    typeof(bool),
                    "true to include items finished for good."
                    + " Default false."));
        api.MapGet(One, GetAsync)
            .WithName("GetItem")
            .Produces<ItemView>()
            .WithSummary("Get an item")
            .Takes()
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapWrite<ItemCreate, ItemView>(
            "POST",
            "/items",
            "CreateItem",
            "Create item",
            StatusCodes.Status201Created,
            CreateAsync);
        api.MapWrite<ItemPatch, ItemView>(
            "PATCH",
            One,
            "EditItem",
            "Edit item",
            StatusCodes.Status200OK,
            EditAsync,
            hints: PatchHints);
        api.MapWrite<DueInput, ItemView>(
            "PUT",
            One + "/due",
            "RescheduleItem",
            "Reschedule item",
            StatusCodes.Status200OK,
            RescheduleAsync);
        api.MapWrite<NoBody, ItemView>(
            "DELETE",
            One + "/due",
            "ClearDueDate",
            "Clear due date",
            StatusCodes.Status200OK,
            ClearDueAsync);
        api.MapWrite<RepeatInput, ItemView>(
            "PUT",
            One + "/repeat",
            "SetRepeat",
            "Set repeat rule",
            StatusCodes.Status200OK,
            RepeatAsync);
        api.MapWrite<NoBody, ItemView>(
            "DELETE",
            One + "/repeat",
            "StopRepeating",
            "Stop repeating",
            StatusCodes.Status200OK,
            StopRepeatingAsync);
        api.MapWrite<CompleteRequest, ItemView>(
            "POST",
            One + "/complete",
            "CompleteItem",
            "Complete item",
            StatusCodes.Status200OK,
            CompleteAsync,
            emptyBody: () => new CompleteRequest());
        api.MapWrite<NoBody, ItemView>(
            "DELETE",
            One,
            "DeleteItem",
            "Delete item",
            StatusCodes.Status200OK,
            DeleteAsync);
        api.MapWrite<NoBody, ItemView>(
            "PUT",
            One + "/contexts/{contextId:guid}",
            "AddItemContext",
            "Add context to item",
            StatusCodes.Status200OK,
            AddContextAsync);
        api.MapWrite<NoBody, ItemView>(
            "DELETE",
            One + "/contexts/{contextId:guid}",
            "RemoveItemContext",
            "Remove context from item",
            StatusCodes.Status200OK,
            RemoveContextAsync);
        api.MapWrite<ReminderInput, ItemView>(
            "POST",
            One + "/reminders",
            "AddReminder",
            "Add reminder",
            StatusCodes.Status201Created,
            AddReminderAsync);
        api.MapWrite<NoBody, ItemView>(
            "DELETE",
            One + "/reminders/{reminderId:guid}",
            "RemoveReminder",
            "Remove reminder",
            StatusCodes.Status200OK,
            RemoveReminderAsync);
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        NudgeDbContext db)
    {
        try
        {
            var query = new Query(http);
            var list = query.Enum<ItemState>("list");
            var projectId = query.Guid("projectId");
            var contextId = query.Guid("contextId");
            var includeCompleted = query.Bool("includeCompleted") ?? false;

            var items = await db.Items.ToListAsync(http.RequestAborted);
            var views = items
                .Where(item => includeCompleted || !item.IsCompleted)
                .Where(item => list is null || item.State == list)
                .Where(
                    item => projectId is null
                        || item.Project?.Id == projectId)
                .Where(
                    item => contextId is null
                        || item.Contexts.Any(c => c.Id == contextId))
                .OrderBy(item => item.DueDate ?? DateOnly.MaxValue)
                .ThenBy(item => item.Id.ToString(), StringComparer.Ordinal)
                .Select(ItemView.Of)
                .ToList();
            return TypedResults.Ok(views);
        }
        catch (ApiProblem problem)
        {
            return problem.ToResult(http);
        }
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        HttpContext http,
        NudgeDbContext db)
    {
        try
        {
            _ = new Query(http);
            var item = await FindAsync(db, id, http.RequestAborted);
            return TypedResults.Ok(ItemView.Of(item));
        }
        catch (ApiProblem problem)
        {
            return problem.ToResult(http);
        }
    }

    private static async Task<WriteOutcome<ItemView>> CreateAsync(
        Write<ItemCreate> write)
    {
        var body = write.Body;
        var item = Domain.Call(() => new Item(body.Title), "title");
        item.Notes = body.Notes ?? "";
        if (body.List is { } list)
        {
            item.State = list;
        }

        if (body.Priority is { } priority)
        {
            item.Priority = priority;
        }

        if (body.ProjectId is { } projectId)
        {
            item.Project = await ProjectAsync(write.Db, projectId, "projectId");
        }

        item.DeferUntil = body.DeferUntil;
        item.DoNotAutoReschedule = body.DoNotAutoReschedule ?? false;

        var contextIds = body.ContextIds ?? [];
        if (contextIds.Distinct().Count() != contextIds.Count)
        {
            throw ApiProblem.Invalid(
                "contextIds",
                "A context is listed more than once.");
        }

        foreach (var contextId in contextIds)
        {
            item.AddContext(
                await write.Db.Contexts.SingleOrDefaultAsync(
                    c => c.Id == contextId)
                ?? throw ApiProblem.Invalid(
                    "contextIds",
                    $"There is no context {contextId}."));
        }

        if (body.Repeat is { } repeat)
        {
            var start = repeat.Start
                ?? body.Due?.Date
                ?? throw ApiProblem.Invalid(
                    "repeat.start",
                    "Give repeat.start, or due.date for the series to"
                    + " start on.");
            if (body.Due is { } due)
            {
                // The time first: Repeat keeps it.
                item.SetDue(start, due.Time);
            }

            Repeat(item, repeat, start);
            if (repeat.Start is not null && body.Due is { } moved)
            {
                item.SetDue(moved.Date, moved.Time);
            }
        }
        else if (body.Due is { } due)
        {
            item.SetDue(due.Date, due.Time);
        }

        var reminders = body.Reminders ?? [];
        for (var i = 0; i < reminders.Count; i++)
        {
            item.AddReminder(MakeReminder(reminders[i], $"reminders[{i}]"));
        }

        write.Db.Items.Add(item);
        return WriteOutcome.Created(() => ItemView.Of(item));
    }

    private static async Task<WriteOutcome<ItemView>> EditAsync(
        Write<ItemPatch> write)
    {
        var body = write.Body;
        var item = await FindAsync(write);
        if (write.Has(nameof(ItemPatch.Title)))
        {
            var title = body.Title ?? throw NotNull(nameof(body.Title));
            Domain.Call(() => item.Title = title, "title");
        }

        if (write.Has(nameof(ItemPatch.Notes)))
        {
            item.Notes = body.Notes ?? throw NotNull(nameof(body.Notes));
        }

        if (write.Has(nameof(ItemPatch.List)))
        {
            item.State = body.List ?? throw NotNull(nameof(body.List));
        }

        if (write.Has(nameof(ItemPatch.Priority)))
        {
            item.Priority = body.Priority
                ?? throw NotNull(nameof(body.Priority));
        }

        if (write.Has(nameof(ItemPatch.ProjectId)))
        {
            item.Project = body.ProjectId is { } projectId
                ? await ProjectAsync(write.Db, projectId, "projectId")
                : null;
        }

        if (write.Has(nameof(ItemPatch.DeferUntil)))
        {
            item.DeferUntil = body.DeferUntil;
        }

        if (write.Has(nameof(ItemPatch.DoNotAutoReschedule)))
        {
            item.DoNotAutoReschedule = body.DoNotAutoReschedule
                ?? throw NotNull(nameof(body.DoNotAutoReschedule));
        }

        return WriteOutcome.Ok(() => ItemView.Of(item));
    }

    private static async Task<WriteOutcome<ItemView>> RescheduleAsync(
        Write<DueInput> write)
    {
        var item = await FindAsync(write);
        item.SetDue(write.Body.Date, write.Body.Time);
        return WriteOutcome.Ok(() => ItemView.Of(item));
    }

    private static async Task<WriteOutcome<ItemView>> ClearDueAsync(
        Write<NoBody> write)
    {
        var item = await FindAsync(write);
        Domain.Call(item.ClearDue, "due");
        return WriteOutcome.Ok(() => ItemView.Of(item));
    }

    private static async Task<WriteOutcome<ItemView>> RepeatAsync(
        Write<RepeatInput> write)
    {
        var item = await FindAsync(write);
        var start = write.Body.Start
            ?? item.DueDate
            ?? throw ApiProblem.Invalid(
                "start",
                "The item has no due date for the series to start on;"
                + " give start.");
        Repeat(item, write.Body, start, "");
        return WriteOutcome.Ok(() => ItemView.Of(item));
    }

    private static async Task<WriteOutcome<ItemView>> StopRepeatingAsync(
        Write<NoBody> write)
    {
        var item = await FindAsync(write);
        item.StopRepeating();
        return WriteOutcome.Ok(() => ItemView.Of(item));
    }

    /// <summary>
    /// Completes the item once. For a repeating item the client
    /// names the occurrence it means, and the request is refused if
    /// that is not the current one; so the same request sent twice,
    /// without an idempotency key, completes one occurrence, not two.
    /// </summary>
    private static async Task<WriteOutcome<ItemView>> CompleteAsync(
        Write<CompleteRequest> write)
    {
        var item = await FindAsync(write);
        var named = write.Body.Occurrence;
        if (item.Recurrence is not null
            && item.OccurrenceDate is { } current)
        {
            if (named is null)
            {
                throw ApiProblem.Invalid(
                    "occurrence",
                    "The item repeats. Give the occurrence being"
                    + $" completed: {Text(current)} is the current one.");
            }

            if (named != current)
            {
                throw ApiProblem.Conflict(
                    $"The occurrence of {Text(named.Value)} is not the"
                    + $" current one, which is {Text(current)}. It may"
                    + " have been completed already.",
                    new Dictionary<string, object?>
                    {
                        ["currentOccurrence"] = Text(current),
                    });
            }
        }
        else if (named is not null)
        {
            throw ApiProblem.Conflict(
                "The item does not repeat, so it has no occurrence to"
                + " name. Read it again before completing it.");
        }

        Domain.Call(() => item.Complete(write.Now, write.Zone), "item");
        return WriteOutcome.Ok(() => ItemView.Of(item));
    }

    private static async Task<WriteOutcome<ItemView>> DeleteAsync(
        Write<NoBody> write)
    {
        var item = await FindAsync(write);
        Domain.Call(() => item.Delete(write.Now), "item");
        return WriteOutcome.Deleted<ItemView>();
    }

    private static async Task<WriteOutcome<ItemView>> AddContextAsync(
        Write<NoBody> write)
    {
        var item = await FindAsync(write);
        item.AddContext(await ContextAsync(write));
        return WriteOutcome.Ok(() => ItemView.Of(item));
    }

    private static async Task<WriteOutcome<ItemView>> RemoveContextAsync(
        Write<NoBody> write)
    {
        var item = await FindAsync(write);
        item.RemoveContext(await ContextAsync(write));
        return WriteOutcome.Ok(() => ItemView.Of(item));
    }

    private static async Task<WriteOutcome<ItemView>> AddReminderAsync(
        Write<ReminderInput> write)
    {
        var item = await FindAsync(write);
        item.AddReminder(MakeReminder(write.Body, "body"));
        return WriteOutcome.Created(() => ItemView.Of(item));
    }

    private static async Task<WriteOutcome<ItemView>> RemoveReminderAsync(
        Write<NoBody> write)
    {
        var item = await FindAsync(write);
        var id = write.RouteGuid("reminderId");
        item.RemoveReminder(
            item.Reminders.SingleOrDefault(r => r.Id == id)
            ?? throw ApiProblem.NotFound("reminder on this item"));
        return WriteOutcome.Ok(() => ItemView.Of(item));
    }

    private static Task<Item> FindAsync<TBody>(Write<TBody> write)
    {
        return FindAsync(
            write.Db,
            write.RouteGuid("id"),
            CancellationToken.None);
    }

    private static async Task<Item> FindAsync(
        NudgeDbContext db,
        Guid id,
        CancellationToken cancellationToken)
    {
        return await db.Items.SingleOrDefaultAsync(
                item => item.Id == id,
                cancellationToken)
            ?? throw ApiProblem.NotFound("item");
    }

    private static async Task<Project> ProjectAsync(
        NudgeDbContext db,
        Guid id,
        string field)
    {
        return await db.Projects.SingleOrDefaultAsync(p => p.Id == id)
            ?? throw ApiProblem.Invalid(
                field,
                $"There is no project {id}, or it has been deleted.");
    }

    private static async Task<Context> ContextAsync(Write<NoBody> write)
    {
        var id = write.RouteGuid("contextId");
        return await write.Db.Contexts.SingleOrDefaultAsync(c => c.Id == id)
            ?? throw ApiProblem.NotFound("context");
    }

    /// <param name="prefix">Where the repeat input sits in the body,
    /// for naming the field in an error.</param>
    private static void Repeat(
        Item item,
        RepeatInput repeat,
        DateOnly start,
        string prefix = "repeat.")
    {
        var rule = Domain.Call(
            () => RecurrenceRule.Parse(repeat.Rule),
            prefix + "rule");
        Domain.Call(
            () => item.Repeat(rule, repeat.Mode, start),
            prefix + "rule");
    }

    private static Reminder MakeReminder(
        ReminderInput input,
        string field)
    {
        return (input.At, input.OffsetMinutes) switch
        {
            ({ } at, null) => Reminder.AtInstant(at),
            (null, { } minutes) => Domain.Call(
                () => Reminder.FromDue(TimeSpan.FromMinutes(minutes)),
                field + ".offsetMinutes"),
            _ => throw ApiProblem.Invalid(
                field,
                "Give exactly one of at and offsetMinutes."),
        };
    }

    private static ApiProblem NotNull(string propertyName)
    {
        return ApiProblem.Invalid(
            Write<ItemPatch>.JsonName(propertyName),
            "Cannot be null. Leave the property out to keep the value.");
    }

    private static string Text(DateOnly date)
    {
        return date.ToString(
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static IReadOnlyDictionary<string, string> Hints(
        params (string Name, string Hint)[] hints)
    {
        return hints.ToDictionary(pair => pair.Name, pair => pair.Hint);
    }
}
