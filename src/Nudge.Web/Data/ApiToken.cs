namespace Nudge.Web.Data;

/// <summary>
/// A token a client of the API authenticates with. Only the SHA-256
/// hash of the token is kept; the token itself is shown once, when
/// it is created, and cannot be recovered from here.
/// </summary>
/// <remarks>
/// Tokens are security state, not the owner's data: like
/// <see cref="IdempotencyRecord"/>, they are not recorded in the
/// change log, so no change ever holds a hash and no undo can bring
/// a revoked token back. They are made and revoked with the
/// <c>token</c> command line, never through the API.
/// </remarks>
public sealed class ApiToken
{
    public ApiToken(
        string name,
        string hash,
        DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7(createdAt);
        Name = name;
        Hash = hash;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// What the token is for, such as "planning-agent". Changes made
    /// with it are recorded as made by <c>api:</c> and this name, so
    /// no two tokens share one, revoked ones included.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>The SHA-256 hash of the token, in hexadecimal.
    /// </summary>
    public string Hash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the token stopped being accepted; null while it
    /// is active. A revoked token stays revoked.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    public void Revoke(DateTimeOffset at)
    {
        if (RevokedAt is not null)
        {
            throw new InvalidOperationException(
                $"The token {Name} is already revoked.");
        }

        RevokedAt = at;
    }
}
