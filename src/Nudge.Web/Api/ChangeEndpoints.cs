using Microsoft.EntityFrameworkCore;
using Nudge.Web.Data;

namespace Nudge.Web.Api;

/// <summary>The change log, and undo.</summary>
internal static class ChangeEndpoints
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/changes", ListAsync)
            .WithName("ListChanges")
            .Produces<List<ChangeView>>()
            .WithSummary("List changes")
            .WithDescription("Newest first.")
            .Takes(
                new QueryParameter(
                    "before",
                    typeof(long),
                    "Only changes numbered lower than this: the"
                    + " number of the last change of the previous"
                    + " page."),
                new QueryParameter(
                    "limit",
                    typeof(long),
                    $"How many, from 1 to {MaxLimit}. Default"
                    + $" {DefaultLimit}."));
        api.MapGet("/changes/{id:long}", GetAsync)
            .WithName("GetChange")
            .Produces<ChangeView>()
            .WithSummary("Get a change")
            .Takes()
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapWrite<NoBody, ChangeView>(
            "POST",
            "/changes/{id:long}/undo",
            "UndoChange",
            "Undo change",
            StatusCodes.Status200OK,
            UndoAsync)
            .WithDescription(
                "Takes the change back, all or nothing, as a new change"
                + " that can itself be undone. Refused (409, with"
                + " reasons) if something it would put back has been"
                + " changed since. change is the undo; data is the"
                + " change that was undone. Undoing a deletion restores;"
                + " undoing a completion reopens.");
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        NudgeDbContext db)
    {
        try
        {
            var query = new Query(http);
            var before = query.Long("before") ?? long.MaxValue;
            var limit = query.Long("limit") ?? DefaultLimit;
            if (limit is < 1 or > MaxLimit)
            {
                throw ApiProblem.Invalid(
                    "limit",
                    $"Must be from 1 to {MaxLimit}.");
            }

            var changes = await db.Changes
                .AsNoTracking()
                .Where(change => change.Id < before)
                .OrderByDescending(change => change.Id)
                .Take((int)limit)
                .ToListAsync(http.RequestAborted);
            return TypedResults.Ok(
                changes.Select(change => ChangeView.Of(change)).ToList());
        }
        catch (ApiProblem problem)
        {
            return problem.ToResult(http);
        }
    }

    private static async Task<IResult> GetAsync(
        long id,
        HttpContext http,
        NudgeDbContext db)
    {
        try
        {
            _ = new Query(http);
            var change = await db.Changes
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        c => c.Id == id,
                        http.RequestAborted)
                ?? throw ApiProblem.NotFound("change");
            return TypedResults.Ok(ChangeView.Of(change));
        }
        catch (ApiProblem problem)
        {
            return problem.ToResult(http);
        }
    }

    internal static async Task<WriteOutcome<ChangeView>> UndoAsync(
        Write<NoBody> write)
    {
        var id = write.RouteLong("id");
        var undone = await write.Db.Changes
                .AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == id)
            ?? throw ApiProblem.NotFound("change");
        Change? undo;
        try
        {
            undo = await write.Db.UndoWithinAsync(id);
        }
        catch (UndoRefusedException e)
        {
            throw ApiProblem.Conflict(
                    "The change cannot be undone.",
                    new Dictionary<string, object?>
                    {
                        ["reasons"] = e.Reasons,
                    })
                .Because(e);
        }

        return new WriteOutcome<ChangeView>(
            StatusCodes.Status200OK,
            () => ChangeView.Of(undone),
            undo ?? throw ApiProblem.NotFound("change"));
    }
}
