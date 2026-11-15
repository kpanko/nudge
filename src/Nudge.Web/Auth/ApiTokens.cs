using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Nudge.Web.Data;

namespace Nudge.Web.Auth;

/// <summary>
/// Making, hashing, finding and revoking API tokens. A token is
/// <c>nudge_</c> followed by 32 random bytes in base64url; only its
/// SHA-256 hash is stored. Saving a token records no change in the
/// change log.
/// </summary>
public static partial class ApiTokens
{
    public const string Prefix = "nudge_";

    private const int RandomBytes = 32;

    /// <summary>32 bytes in base64url, without padding.</summary>
    private const int EncodedLength = 43;

    /// <summary>
    /// Makes a token, stores its hash under <paramref name="name"/>,
    /// and returns the token and its row. This is the only time the
    /// token exists outside the client that is given it.
    /// </summary>
    /// <exception cref="TokenException">The name is not allowed, or
    /// a token already has it.</exception>
    public static async Task<(string Token, ApiToken Row)> CreateAsync(
        NudgeDbContext db,
        string name,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (NameProblem(name) is { } problem)
        {
            throw new TokenException(problem);
        }

        if (await db.ApiTokens.AnyAsync(
                token => token.Name == name,
                cancellationToken))
        {
            throw new TokenException(
                $"A token named {name} already exists. Names are not"
                + " reused, even after a token is revoked, so that the"
                + " change log names one token.");
        }

        var token = Prefix
            + Base64Url.EncodeToString(
                RandomNumberGenerator.GetBytes(RandomBytes));
        var row = new ApiToken(name, Hash(token), now);
        db.ApiTokens.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return (token, row);
    }

    /// <summary>Every token, oldest first.</summary>
    public static Task<List<ApiToken>> ListAsync(
        NudgeDbContext db,
        CancellationToken cancellationToken = default)
    {
        return db.ApiTokens
            .AsNoTracking()
            .OrderBy(token => token.CreatedAt)
            .ThenBy(token => token.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Revokes the token with this id, or else with this name. It is
    /// refused on the next request that sends it.
    /// </summary>
    /// <exception cref="TokenException">There is no such token, or
    /// it is already revoked.</exception>
    public static async Task<ApiToken> RevokeAsync(
        NudgeDbContext db,
        string nameOrId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var row = Guid.TryParse(nameOrId, out var id)
            ? await db.ApiTokens.SingleOrDefaultAsync(
                token => token.Id == id,
                cancellationToken)
            : await db.ApiTokens.SingleOrDefaultAsync(
                token => token.Name == nameOrId,
                cancellationToken);
        if (row is null)
        {
            throw new TokenException(
                $"There is no token with the name or id {nameOrId}.");
        }

        if (row.RevokedAt is not null)
        {
            throw new TokenException(
                $"The token {row.Name} was already revoked at"
                + $" {Instant(row.RevokedAt.Value)}.");
        }

        row.Revoke(now);
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }

    /// <summary>The id and name of the active token this is, or
    /// null if it is no such token.</summary>
    public static async Task<(Guid Id, string Name)?> FindActiveAsync(
        NudgeDbContext db,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (!IsWellFormed(token))
        {
            return null;
        }

        var hash = Hash(token);
        var found = await db.ApiTokens
            .AsNoTracking()
            .Where(row => row.Hash == hash && row.RevokedAt == null)
            .Select(row => new { row.Id, row.Name })
            .SingleOrDefaultAsync(cancellationToken);
        return found is null ? null : (found.Id, found.Name);
    }

    /// <summary>The SHA-256 hash of the token's text, in upper-case
    /// hexadecimal, as it is stored.</summary>
    public static string Hash(string token)
    {
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>Why a name is not allowed, or null if it is.
    /// </summary>
    public static string? NameProblem(string name)
    {
        if (!NamePattern().IsMatch(name))
        {
            return $"A token's name is 1 to"
                + $" {ApiTokenConfiguration.MaxNameLength} letters,"
                + " digits, '-', '_' or '.', such as planning-agent.";
        }

        // Revoking takes a name or an id; a name that reads as an id
        // would be ambiguous.
        if (Guid.TryParse(name, out _))
        {
            return "A token's name cannot look like an id.";
        }

        return null;
    }

    /// <summary>An instant as the command line shows it.</summary>
    public static string Instant(DateTimeOffset at)
    {
        return at.UtcDateTime.ToString(
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool IsWellFormed(string token)
    {
        return token.Length == Prefix.Length + EncodedLength
            && token.StartsWith(Prefix, StringComparison.Ordinal)
            && TokenPattern().IsMatch(token[Prefix.Length..]);
    }

    // \z, not $, which would let a final line break through.
    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}\z")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^[A-Za-z0-9_-]+\z")]
    private static partial Regex TokenPattern();
}

/// <summary>A token operation that cannot be carried out, with a
/// message for whoever asked for it.</summary>
public sealed class TokenException(string message) : Exception(message);
