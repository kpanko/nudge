namespace Nudge.Web.Api;

/// <summary>
/// A request that cannot be carried out, and the problem details
/// (RFC 9457) to answer it with. Thrown by the API's own code and
/// turned into a response before anything is committed.
/// </summary>
public sealed class ApiProblem : Exception
{
    public ApiProblem(
        int status,
        string title,
        string? detail = null,
        IDictionary<string, string[]>? errors = null,
        IDictionary<string, object?>? extensions = null)
        : base(detail ?? title)
    {
        Status = status;
        Title = title;
        Detail = detail;
        Errors = errors;
        Extensions = extensions;
    }

    public int Status { get; }

    public string Title { get; }

    public string? Detail { get; }

    /// <summary>For a 400: what is wrong with each input, keyed by
    /// the name of the property or parameter.</summary>
    public IDictionary<string, string[]>? Errors { get; }

    public IDictionary<string, object?>? Extensions { get; }

    /// <summary>A 400 for one input.</summary>
    public static ApiProblem Invalid(
        string field,
        string message)
    {
        return new ApiProblem(
            StatusCodes.Status400BadRequest,
            "The request is not valid.",
            errors: new Dictionary<string, string[]>
            {
                [field] = [message],
            });
    }

    public static ApiProblem NotFound(string what)
    {
        return new ApiProblem(
            StatusCodes.Status404NotFound,
            "Not found.",
            $"There is no {what} with this id, or it has been deleted.");
    }

    /// <summary>A 409: the request is well formed, but the data as it
    /// stands does not allow it.</summary>
    public static ApiProblem Conflict(
        string detail,
        IDictionary<string, object?>? extensions = null)
    {
        return new ApiProblem(
            StatusCodes.Status409Conflict,
            "The request conflicts with the data as it stands.",
            detail,
            extensions: extensions);
    }

    public IResult ToResult()
    {
        if (Errors is not null)
        {
            return Results.ValidationProblem(
                Errors,
                Detail,
                title: Title,
                statusCode: Status,
                extensions: Extensions);
        }

        return Results.Problem(
            Detail,
            statusCode: Status,
            title: Title,
            extensions: Extensions);
    }
}
