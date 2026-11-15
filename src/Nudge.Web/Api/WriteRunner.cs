using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nudge.Web.Data;

namespace Nudge.Web.Api;

/// <summary>
/// Carries out every write the API accepts, the same way, so that no
/// write endpoint can skip a guarantee:
/// <list type="bullet">
/// <item>The query and the body are checked strictly; anything not
/// understood is a 400, never ignored.</item>
/// <item>A transaction is taken before anything is read, which in
/// SQLite holds the write lock, so the change log's "before" values
/// are true and two writes cannot interleave.</item>
/// <item>Who made the change comes from one place,
/// <see cref="ChangeSource"/>.</item>
/// <item>With <c>?dryRun=true</c> the work is done and saved, so the
/// database's constraints are checked, and then rolled back.</item>
/// <item>With an <c>Idempotency-Key</c>, the response is stored in
/// the same transaction as the change, and the same request sent
/// again gets that response back without anything being done.</item>
/// </list>
/// </summary>
public sealed class WriteRunner(
    NudgeDbContext db,
    ChangeSource source,
    TimeProvider clock,
    OwnerTimeZone zone)
{
    public const string DryRunParameter = "dryRun";
    public const string KeyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    /// <summary>The largest body a write accepts, in bytes.</summary>
    public const int MaxBodyBytes = 1 << 20;

    public async Task<IResult> RunAsync<TBody, TData>(
        HttpContext http,
        WriteEndpoint endpoint,
        Func<Write<TBody>, Task<WriteOutcome<TData>>> work)
    {
        var cancellationToken = http.RequestAborted;
        try
        {
            var dryRun = DryRun(http.Request.Query);
            var key = Key(http.Request.Headers);
            var bytes = await BodyAsync(http.Request, cancellationToken);
            var (body, present) = Parse<TBody>(endpoint, bytes);
            var method = http.Request.Method;
            var path = http.Request.Path.Value ?? "";
            var fingerprint = Fingerprint(method, path, bytes);

            db.Source = source.For(http);
            db.Description = endpoint.Description;

            // Taken before anything is read; see above.
            await using var transaction = await db.Database
                .BeginTransactionAsync(cancellationToken);

            if (key is not null
                && await db.IdempotencyRecords
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        record => record.Key == key,
                        cancellationToken)
                    is { } earlier)
            {
                if (earlier.Fingerprint != fingerprint)
                {
                    throw new ApiProblem(
                        StatusCodes.Status422UnprocessableEntity,
                        "The idempotency key was used for another request.",
                        $"The key was first sent with {earlier.Method}"
                        + $" {earlier.Path} and a body that may differ."
                        + " Use a new key for a new request.");
                }

                http.Response.Headers[ReplayedHeader] = "true";
                return Respond(earlier.Status, earlier.Body);
            }

            var outcome = await work(
                new Write<TBody>(
                    db,
                    body,
                    present,
                    http,
                    clock.GetUtcNow(),
                    zone.Zone));
            await SaveAsync(cancellationToken);

            var change = outcome.Change ?? db.LastChange;
            var result = new WriteResult<TData>(
                dryRun,
                change is null ? null : ChangeView.Of(change, dryRun),
                outcome.Data());
            var text = JsonSerializer.Serialize(result, JsonSetup.Options);

            if (dryRun)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Respond(StatusCodes.Status200OK, text);
            }

            if (key is not null)
            {
                db.IdempotencyRecords.Add(
                    new IdempotencyRecord(
                        key,
                        method,
                        path,
                        fingerprint,
                        outcome.Status,
                        text,
                        change?.Id,
                        clock.GetUtcNow()));
                await SaveAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return Respond(outcome.Status, text);
        }
        catch (ApiProblem problem)
        {
            // The transaction, if one was begun, has been rolled back
            // by leaving its block.
            return problem.ToResult(http);
        }
    }

    /// <summary>The response, written the same way the first time
    /// and on every replay, so the two are byte for byte the same.
    /// </summary>
    private static IResult Respond(
        int status,
        string body)
    {
        return Results.Text(
            body,
            "application/json; charset=utf-8",
            Encoding.UTF8,
            status);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (Domain.Refused(e) is not null)
        {
            throw Domain.Refused(e)!;
        }
    }

    /// <summary>
    /// Whether this is a dry run. <c>dryRun</c> is the one query
    /// parameter a write takes, and it must be <c>true</c> or
    /// <c>false</c>: a misspelt parameter that was ignored would turn
    /// a dry run into a real write.
    /// </summary>
    private static bool DryRun(IQueryCollection query)
    {
        foreach (var (name, _) in query)
        {
            if (name != DryRunParameter)
            {
                throw ApiProblem.Invalid(
                    name,
                    $"Unknown query parameter. The only one a write"
                    + $" takes is {DryRunParameter}.");
            }
        }

        if (!query.TryGetValue(DryRunParameter, out var values))
        {
            return false;
        }

        return values.Count == 1 && values[0] is "true" or "false"
            ? values[0] == "true"
            : throw ApiProblem.Invalid(
                DryRunParameter,
                "Must be given once, as true or false.");
    }

    private static string? Key(IHeaderDictionary headers)
    {
        if (!headers.TryGetValue(KeyHeader, out var values))
        {
            return null;
        }

        if (values.Count != 1
            || values[0] is not { Length: > 0 } key
            || key.Length > IdempotencyRecordConfiguration.MaxKeyLength
            || key.Any(c => c < '!' || c > '~'))
        {
            throw ApiProblem.Invalid(
                KeyHeader,
                "Must be given once, as 1 to"
                + $" {IdempotencyRecordConfiguration.MaxKeyLength}"
                + " printable ASCII characters without spaces, such as"
                + " a UUID.");
        }

        return key;
    }

    private static async Task<byte[]> BodyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxBodyBytes)
        {
            throw TooLarge();
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(
                   chunk,
                   cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBodyBytes)
            {
                throw TooLarge();
            }
        }

        var bytes = buffer.ToArray();
        if (bytes.Length > 0 && !IsJson(request.ContentType))
        {
            throw new ApiProblem(
                StatusCodes.Status415UnsupportedMediaType,
                "The body must be JSON.",
                "Send it with Content-Type: application/json.");
        }

        return bytes;
    }

    private static ApiProblem TooLarge()
    {
        return new ApiProblem(
            StatusCodes.Status413PayloadTooLarge,
            "The body is too large.",
            $"A write takes at most {MaxBodyBytes} bytes.");
    }

    private static bool IsJson(string? contentType)
    {
        return MediaTypeHeaderValue.TryParse(contentType, out var type)
            && type.MediaType == "application/json"
            && (type.CharSet is null
                || type.CharSet.Equals(
                    "utf-8",
                    StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The body as <typeparamref name="TBody"/>, and the names of
    /// the properties it gave. A property the type does not have is
    /// refused by name, with a pointer to the endpoint that does
    /// take it when there is one.
    /// </summary>
    private static (TBody Body, IReadOnlySet<string> Present) Parse<TBody>(
        WriteEndpoint endpoint,
        byte[] bytes)
    {
        if (typeof(TBody) == typeof(NoBody))
        {
            if (bytes.Length > 0)
            {
                throw ApiProblem.Invalid(
                    "body",
                    "This request takes no body.");
            }

            return ((TBody)(object)NoBody.Value, new HashSet<string>());
        }

        if (bytes.Length == 0)
        {
            if (endpoint.EmptyBody is { } empty)
            {
                return ((TBody)empty(), new HashSet<string>());
            }

            throw ApiProblem.Invalid("body", "A JSON body is required.");
        }

        var present = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw ApiProblem.Invalid(
                    "body",
                    "The body must be a JSON object.");
            }

            var known = JsonSetup.Options
                .GetTypeInfo(typeof(TBody))
                .Properties
                .Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!known.Contains(property.Name))
                {
                    throw ApiProblem.Invalid(
                        property.Name,
                        "Unknown property."
                        + (endpoint.Hints.TryGetValue(
                                property.Name,
                                out var hint)
                            ? $" {hint}"
                            : ""));
                }

                present.Add(property.Name);
            }

            var body = JsonSerializer.Deserialize<TBody>(
                bytes,
                JsonSetup.Options);
            return (body!, present);
        }
        catch (JsonException e)
        {
            throw ApiProblem.Invalid(Field(e.Path), e.Message);
        }
    }

    /// <summary>"$.repeat.rule" as "repeat.rule"; the body itself as
    /// "body".</summary>
    private static string Field(string? path)
    {
        return path is null or "$"
            ? "body"
            : path.StartsWith("$.", StringComparison.Ordinal)
                ? path[2..]
                : path;
    }

    /// <summary>
    /// What makes two requests the same request: the method, the
    /// path and the exact bytes of the body. The query is left out;
    /// <c>dryRun</c> is the only parameter a write takes, so a dry
    /// run and the real request count as the same. The path is read
    /// as routing reads it, ignoring case and a trailing slash, so
    /// an id in upper case is the same request; an id spelt another
    /// way (without hyphens, in braces) is not.
    /// </summary>
    private static string Fingerprint(
        string method,
        string path,
        byte[] body)
    {
        var routed = path.ToLowerInvariant();
        if (routed.Length > 1)
        {
            routed = routed.TrimEnd('/');
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{method}\n{routed}\n"));
        hash.AppendData(body);
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

/// <summary>Who changes are recorded as made by.</summary>
public sealed class ChangeSource
{
    /// <summary>
    /// The name recorded with every change a request makes:
    /// <c>api:</c> and the name of the token it was made with. This
    /// is the one place it is decided; the web UI (M3) will add a
    /// name for the signed-in owner.
    /// </summary>
    /// <exception cref="InvalidOperationException">The request was
    /// not authenticated, which the fallback policy rules out before
    /// a write is reached.</exception>
    public string For(HttpContext http)
    {
        var token = Auth.ApiTokenAuthentication.TokenName(http.User)
            ?? throw new InvalidOperationException(
                "A write was reached without an API token.");
        return $"api:{token}";
    }
}

/// <summary>
/// Marks an endpoint as a write that goes through
/// <see cref="WriteRunner"/>. The tests find every write endpoint by
/// it, and fail for one that does not carry it.
/// </summary>
/// <param name="Description">Recorded with the change, such as
/// "Complete item".</param>
/// <param name="Body">The type of the body, or
/// <see cref="NoBody"/>.</param>
/// <param name="EmptyBody">Makes the body to use when none is sent;
/// null if one is required.</param>
/// <param name="Hints">For properties the body does not take, where
/// to go instead.</param>
public sealed record WriteEndpoint(
    string Description,
    Type Body,
    Func<object>? EmptyBody,
    IReadOnlyDictionary<string, string> Hints);
