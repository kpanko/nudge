namespace Nudge.Web.Auth;

/// <summary>
/// Who may sign in to the web UI, and with which Google client. All
/// of it is deployment configuration (Fly secrets), never in the
/// repository:
/// <list type="bullet">
/// <item><c>Nudge:Google:ClientId</c> and
/// <c>Nudge:Google:ClientSecret</c>: the OAuth client.</item>
/// <item><c>Nudge:Owner:Email</c>: the one Google account allowed,
/// compared ignoring case, and only when Google says the address is
/// verified.</item>
/// <item><c>Nudge:Owner:Subject</c>, optional: that account's
/// Google id (<c>sub</c>), which pins it further.</item>
/// </list>
/// Without the first three the app still starts, the API works, and
/// every page of the web UI stays locked.
/// </summary>
public sealed class OwnerSignIn
{
    public const string ClientIdSetting = "Nudge:Google:ClientId";
    public const string ClientSecretSetting = "Nudge:Google:ClientSecret";
    public const string EmailSetting = "Nudge:Owner:Email";
    public const string SubjectSetting = "Nudge:Owner:Subject";

    private OwnerSignIn(
        string? clientId,
        string? clientSecret,
        string? email,
        string? subject)
    {
        ClientId = clientId;
        ClientSecret = clientSecret;
        Email = email;
        Subject = subject;
    }

    public string? ClientId { get; }

    public string? ClientSecret { get; }

    /// <summary>The owner's Google address.</summary>
    public string? Email { get; }

    /// <summary>The owner's Google id, if it is pinned.</summary>
    public string? Subject { get; }

    /// <summary>Whether sign-in can work: a client and an owner are
    /// both set.</summary>
    public bool Configured =>
        ClientId is not null && ClientSecret is not null && Email is not null;

    public static OwnerSignIn From(IConfiguration configuration)
    {
        return new OwnerSignIn(
            Value(configuration, ClientIdSetting),
            Value(configuration, ClientSecretSetting),
            Value(configuration, EmailSetting),
            Value(configuration, SubjectSetting));
    }

    /// <summary>
    /// Whether an account Google vouched for is the owner's. Nobody
    /// is, unless sign-in is configured.
    /// </summary>
    /// <param name="email">The address Google gave.</param>
    /// <param name="emailVerified">Whether Google says the address
    /// belongs to the account.</param>
    /// <param name="subject">The account's Google id.</param>
    public bool Allows(
        string? email,
        bool emailVerified,
        string? subject)
    {
        return Configured
            && emailVerified
            && !string.IsNullOrWhiteSpace(email)
            && string.Equals(
                email.Trim(),
                Email,
                StringComparison.OrdinalIgnoreCase)
            && (Subject is null
                || string.Equals(subject, Subject, StringComparison.Ordinal));
    }

    private static string? Value(
        IConfiguration configuration,
        string key)
    {
        var value = configuration[key]?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
