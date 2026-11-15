using Microsoft.EntityFrameworkCore;
using Nudge.Core;
using Nudge.Web.Data;

namespace Nudge.Web.Api;

/// <summary>Contexts, such as "phone" or "errands". Names are
/// unique, whatever the case of their letters.</summary>
internal static class ContextEndpoints
{
    private const string One = "/contexts/{id:guid}";

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/contexts", ListAsync)
            .WithName("ListContexts")
            .Produces<List<ContextView>>()
            .WithSummary("List contexts")
            .ProducesValidationProblem();
        api.MapGet(One, GetAsync)
            .WithName("GetContext")
            .Produces<ContextView>()
            .WithSummary("Get a context")
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapWrite<ContextCreate, ContextView>(
            "POST",
            "/contexts",
            "CreateContext",
            "Create context",
            StatusCodes.Status201Created,
            CreateAsync);
        api.MapWrite<ContextPatch, ContextView>(
            "PATCH",
            One,
            "EditContext",
            "Edit context",
            StatusCodes.Status200OK,
            EditAsync);
        api.MapWrite<NoBody, ContextView>(
            "DELETE",
            One,
            "DeleteContext",
            "Delete context",
            StatusCodes.Status200OK,
            DeleteAsync)
            .WithDescription(
                "Deletes the context. Items keep it, hidden, and show it"
                + " again if the deletion is undone.");
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        NudgeDbContext db)
    {
        try
        {
            _ = new Query(http.Request.Query);
            var contexts = await db.Contexts.ToListAsync(http.RequestAborted);
            return TypedResults.Ok(
                contexts
                    .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(ContextView.Of)
                    .ToList());
        }
        catch (ApiProblem problem)
        {
            return problem.ToResult();
        }
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        HttpContext http,
        NudgeDbContext db)
    {
        try
        {
            _ = new Query(http.Request.Query);
            var context = await db.Contexts.SingleOrDefaultAsync(
                    c => c.Id == id,
                    http.RequestAborted)
                ?? throw ApiProblem.NotFound("context");
            return TypedResults.Ok(ContextView.Of(context));
        }
        catch (ApiProblem problem)
        {
            return problem.ToResult();
        }
    }

    private static Task<WriteOutcome<ContextView>> CreateAsync(
        Write<ContextCreate> write)
    {
        var context = Domain.Call(
            () => new Context(write.Body.Name),
            "name");
        write.Db.Contexts.Add(context);
        return Task.FromResult(
            WriteOutcome.Created(() => ContextView.Of(context)));
    }

    private static async Task<WriteOutcome<ContextView>> EditAsync(
        Write<ContextPatch> write)
    {
        var context = await FindAsync(write);
        if (write.Has(nameof(ContextPatch.Name)))
        {
            var name = write.Body.Name
                ?? throw ApiProblem.Invalid(
                    "name",
                    "Cannot be null. Leave the property out to keep"
                    + " the name.");
            Domain.Call(() => context.Name = name, "name");
        }

        return WriteOutcome.Ok(() => ContextView.Of(context));
    }

    private static async Task<WriteOutcome<ContextView>> DeleteAsync(
        Write<NoBody> write)
    {
        var context = await FindAsync(write);
        Domain.Call(() => context.Delete(write.Now), "context");
        return WriteOutcome.Deleted<ContextView>();
    }

    private static async Task<Context> FindAsync<TBody>(Write<TBody> write)
    {
        var id = write.RouteGuid("id");
        return await write.Db.Contexts.SingleOrDefaultAsync(c => c.Id == id)
            ?? throw ApiProblem.NotFound("context");
    }
}
