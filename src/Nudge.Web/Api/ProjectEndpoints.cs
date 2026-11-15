using Microsoft.EntityFrameworkCore;
using Nudge.Core;
using Nudge.Web.Data;

namespace Nudge.Web.Api;

/// <summary>Projects, which nest to any depth.</summary>
internal static class ProjectEndpoints
{
    private const string One = "/projects/{id:guid}";

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/projects", ListAsync)
            .WithName("ListProjects")
            .Produces<List<ProjectView>>()
            .WithSummary("List projects")
            .ProducesValidationProblem();
        api.MapGet(One, GetAsync)
            .WithName("GetProject")
            .Produces<ProjectView>()
            .WithSummary("Get a project")
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapWrite<ProjectCreate, ProjectView>(
            "POST",
            "/projects",
            "CreateProject",
            "Create project",
            StatusCodes.Status201Created,
            CreateAsync);
        api.MapWrite<ProjectPatch, ProjectView>(
            "PATCH",
            One,
            "EditProject",
            "Edit project",
            StatusCodes.Status200OK,
            EditAsync);
        api.MapWrite<NoBody, ProjectView>(
            "DELETE",
            One,
            "DeleteProject",
            "Delete project",
            StatusCodes.Status200OK,
            DeleteAsync)
            .WithDescription(
                "Deletes the project, the projects under it at any"
                + " depth and the items in all of them, as one change"
                + " that one undo brings back.");
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        NudgeDbContext db)
    {
        try
        {
            _ = new Query(http.Request.Query);
            var projects = await db.Projects.ToListAsync(http.RequestAborted);
            return TypedResults.Ok(
                projects
                    .OrderBy(p => p.Id.ToString(), StringComparer.Ordinal)
                    .Select(ProjectView.Of)
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
            var projects = await db.Projects.ToListAsync(http.RequestAborted);
            var project = projects.SingleOrDefault(p => p.Id == id)
                ?? throw ApiProblem.NotFound("project");
            return TypedResults.Ok(ProjectView.Of(project));
        }
        catch (ApiProblem problem)
        {
            return problem.ToResult();
        }
    }

    private static async Task<WriteOutcome<ProjectView>> CreateAsync(
        Write<ProjectCreate> write)
    {
        var projects = await write.Db.Projects.ToListAsync();
        var parent = write.Body.ParentId is { } parentId
            ? Parent(projects, parentId)
            : null;
        var project = Domain.Call(
            () => new Project(write.Body.Name, parent),
            "name");
        write.Db.Projects.Add(project);
        return WriteOutcome.Created(() => ProjectView.Of(project));
    }

    private static async Task<WriteOutcome<ProjectView>> EditAsync(
        Write<ProjectPatch> write)
    {
        // All of them, so that a loop through any ancestor is seen.
        var projects = await write.Db.Projects.ToListAsync();
        var id = write.RouteGuid("id");
        var project = projects.SingleOrDefault(p => p.Id == id)
            ?? throw ApiProblem.NotFound("project");

        if (write.Has(nameof(ProjectPatch.Name)))
        {
            var name = write.Body.Name
                ?? throw ApiProblem.Invalid(
                    "name",
                    "Cannot be null. Leave the property out to keep"
                    + " the name.");
            Domain.Call(() => project.Name = name, "name");
        }

        if (write.Has(nameof(ProjectPatch.ParentId)))
        {
            var parent = write.Body.ParentId is { } parentId
                ? Parent(projects, parentId)
                : null;
            Domain.Call(() => project.MoveUnder(parent), "parentId");
        }

        return WriteOutcome.Ok(() => ProjectView.Of(project));
    }

    private static async Task<WriteOutcome<ProjectView>> DeleteAsync(
        Write<NoBody> write)
    {
        if (!await write.Db.DeleteProjectAsync(
                write.RouteGuid("id"),
                write.Now))
        {
            throw ApiProblem.NotFound("project");
        }

        return WriteOutcome.Deleted<ProjectView>();
    }

    private static Project Parent(
        List<Project> projects,
        Guid id)
    {
        return projects.SingleOrDefault(p => p.Id == id)
            ?? throw ApiProblem.Invalid(
                "parentId",
                $"There is no project {id}, or it has been deleted.");
    }
}
