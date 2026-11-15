namespace Nudge.Web.Api;

/// <summary>How write endpoints are mapped: always through
/// <see cref="WriteRunner"/>, and always described the same way in
/// the OpenAPI document.</summary>
internal static class WriteEndpoints
{
    private static readonly IReadOnlyDictionary<string, string> NoHints =
        new Dictionary<string, string>();

    /// <param name="name">The operation id, such as
    /// "CompleteItem".</param>
    /// <param name="description">Recorded with each change, and the
    /// operation's summary.</param>
    /// <param name="status">The status of a real write; a dry run
    /// answers 200.</param>
    /// <param name="emptyBody">The body to use when none is sent, if
    /// the body is optional.</param>
    /// <param name="hints">For properties the body does not take,
    /// where to go instead.</param>
    public static RouteHandlerBuilder MapWrite<TBody, TData>(
        this IEndpointRouteBuilder routes,
        string method,
        string pattern,
        string name,
        string description,
        int status,
        Func<Write<TBody>, Task<WriteOutcome<TData>>> work,
        Func<TBody>? emptyBody = null,
        IReadOnlyDictionary<string, string>? hints = null)
        where TBody : class
    {
        var endpoint = new WriteEndpoint(
            description,
            typeof(TBody),
            emptyBody is null ? null : () => emptyBody(),
            hints ?? NoHints);
        var builder = routes
            .MapMethods(
                pattern,
                [method],
                (HttpContext http, WriteRunner runner) =>
                    runner.RunAsync(http, endpoint, work))
            .WithMetadata(endpoint)
            .WithName(name)
            .WithSummary(description)
            .Produces<WriteResult<TData>>(status)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        if (status != StatusCodes.Status200OK)
        {
            // What a dry run answers.
            builder.Produces<WriteResult<TData>>(StatusCodes.Status200OK);
        }

        if (typeof(TBody) != typeof(NoBody))
        {
            builder.Accepts<TBody>(
                isOptional: emptyBody is not null,
                "application/json");
        }

        return builder;
    }
}
