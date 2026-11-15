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

    /// <summary>
    /// The exception this problem was made from, when it was made
    /// from one: a refusal by <c>Nudge.Core</c> or by the database.
    /// It is logged, never sent. Core signals its rules with the
    /// same exception types a bug inside it would throw, so the log
    /// is where such a bug, answered as a 400 or 409, can be seen.
    /// </summary>
    public Exception? Cause { get; private set; }

    /// <summary>Records what this problem was made from.</summary>
    public ApiProblem Because(Exception cause)
    {
        Cause = cause;
        return this;
    }

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

    /// <summary>
    /// The response. A problem made from an exception is logged
    /// first, at Warning, with the exception; the request body is
    /// not logged.
    /// </summary>
    public IResult ToResult(HttpContext http)
    {
        if (Cause is not null)
        {
            http.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(ApiProblem))
                .LogWarning(
                    Cause,
                    "{Method} {Path} was answered {Status}: {Problem}",
                    http.Request.Method,
                    http.Request.Path.Value,
                    Status,
                    Detail ?? Title);
        }

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
