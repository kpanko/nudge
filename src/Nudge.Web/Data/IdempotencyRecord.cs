namespace Nudge.Web.Data;

/// <summary>
/// A write request that came with an <c>Idempotency-Key</c>, and the
/// response it was given, so that the same request sent again is
/// answered from here instead of being carried out twice. It is
/// written in the same transaction as the change the request made.
/// </summary>
/// <remarks>
/// These rows are bookkeeping about requests, not the owner's data:
/// they are not recorded in the change log, and undoing a change
/// leaves its record in place. A request retried after its change
/// was undone is still answered from here, and is not carried out
/// again.
/// </remarks>
public sealed class IdempotencyRecord
{
    public IdempotencyRecord(
        string key,
        string method,
        string path,
        string fingerprint,
        int status,
        string body,
        long? changeId,
        DateTimeOffset createdAt)
    {
        Key = key;
        Method = method;
        Path = path;
        Fingerprint = fingerprint;
        Status = status;
        Body = body;
        ChangeId = changeId;
        CreatedAt = createdAt;
    }

    /// <summary>The key the client sent.</summary>
    public string Key { get; private set; }

    public string Method { get; private set; }

    public string Path { get; private set; }

    /// <summary>
    /// A hash of the method, the path and the exact bytes of the
    /// body. The same key with a different fingerprint is a
    /// different request, and is refused.
    /// </summary>
    public string Fingerprint { get; private set; }

    /// <summary>The status code of the response.</summary>
    public int Status { get; private set; }

    /// <summary>The body of the response, exactly as it was sent.
    /// </summary>
    public string Body { get; private set; }

    /// <summary>The change the request made, or null if it changed
    /// nothing.</summary>
    public long? ChangeId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
