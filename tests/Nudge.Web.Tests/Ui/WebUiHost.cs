using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nudge.Web.Auth;
using Nudge.Web.Tests.Api;

namespace Nudge.Web.Tests.Ui;

/// <summary>
/// The real app with sign-in configured for an invented owner and
/// client, Google replaced by <see cref="FakeGoogle"/>, and a client
/// that follows no redirects and keeps cookies itself, so that each
/// step of a sign-in can be looked at. Requests go to
/// <c>https://localhost</c>, since the cookies are sent over HTTPS
/// only.
/// </summary>
internal sealed partial class WebUiHost : IDisposable
{
    public const string OwnerEmail = "owner@example.test";
    public const string OwnerSubject = "100000000000000000001";
    public const string ClientId = FakeGoogle.ClientId;
    public const string ClientSecret = "not-a-real-secret";

    private static readonly IReadOnlyDictionary<string, string?> NoSettings =
        new Dictionary<string, string?>();

    private readonly WebApplicationFactory<Program> _app;
    private string? _token;

    /// <param name="settings">Settings to use instead of, or besides,
    /// the configured sign-in; a null value removes one.</param>
    /// <param name="configured">False to start with no sign-in
    /// configured at all.</param>
    /// <param name="fakeGoogle">False to leave the Google scheme as
    /// the app sets it up, for looking at, not for signing in.
    /// </param>
    public WebUiHost(
        IReadOnlyDictionary<string, string?>? settings = null,
        bool configured = true,
        bool fakeGoogle = true,
        DateTimeOffset? now = null)
    {
        var all = new Dictionary<string, string?>();
        if (configured)
        {
            all[OwnerSignIn.ClientIdSetting] = ClientId;
            all[OwnerSignIn.ClientSecretSetting] = ClientSecret;
            all[OwnerSignIn.EmailSetting] = OwnerEmail;
        }

        foreach (var (key, value) in settings ?? NoSettings)
        {
            all[key] = value;
        }

        Factory = new NudgeWebFactory
        {
            Clock = now is { } fixedNow ? new FixedClock(fixedNow) : null,
        };
        _app = Factory.WithWebHostBuilder(
            builder =>
            {
                foreach (var (key, value) in all)
                {
                    builder.UseSetting(key, value);
                }

                builder.ConfigureLogging(
                    logging => logging.AddProvider(Logs));
                if (fakeGoogle)
                {
                    builder.ConfigureTestServices(
                        services => services.Configure<OpenIdConnectOptions>(
                            OwnerAuthentication.GoogleScheme,
                            Google.Configure));
                }
            });
        Client = CreateClient(new Uri("https://localhost"));
    }

    public FakeGoogle Google { get; } = new();

    /// <summary>Another client of the same app, sending to
    /// <paramref name="baseAddress"/>, following no redirects and
    /// keeping no cookies.</summary>
    public HttpClient CreateClient(Uri baseAddress)
    {
        return _app.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false,
                BaseAddress = baseAddress,
            });
    }

    public NudgeWebFactory Factory { get; }

    public HttpClient Client { get; }

    public LogSink Logs { get; } = new();

    public IServiceProvider Services => _app.Services;

    /// <summary>The cookies the app has set and not removed, by
    /// name.</summary>
    public Dictionary<string, string> Cookies { get; } = new();

    public void Dispose()
    {
        Client.Dispose();
        _app.Dispose();
        Factory.Dispose();
    }

    /// <summary>
    /// Sends a request with the cookies kept so far (unless told
    /// not to), and keeps what the answer sets.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content = null,
        bool withCookies = true,
        string? authorization = null)
    {
        using var message = new HttpRequestMessage(method, path)
        {
            Content = content,
        };
        if (withCookies && Cookies.Count > 0)
        {
            message.Headers.Add(
                "Cookie",
                string.Join(
                    "; ",
                    Cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
        }

        if (authorization is not null)
        {
            message.Headers.TryAddWithoutValidation(
                "Authorization",
                authorization);
        }

        var response = await Client.SendAsync(message);
        Keep(response);
        return response;
    }

    public Task<HttpResponseMessage> GetAsync(
        string path,
        bool withCookies = true)
    {
        return SendAsync(HttpMethod.Get, path, withCookies: withCookies);
    }

    /// <summary>
    /// Signs in through the real handler: <c>/signin</c> sends the
    /// browser to Google, Google sends it back to the callback with
    /// a code, and the app exchanges the code for an id token. The
    /// answer of the callback is returned.
    /// </summary>
    public async Task<HttpResponseMessage> SignInAsync(
        string? email = OwnerEmail,
        bool emailVerified = true,
        string subject = OwnerSubject,
        string returnUrl = "/",
        string issuer = FakeGoogle.Issuer)
    {
        var challenge = await GetAsync(
            "/signin?returnUrl=" + Uri.EscapeDataString(returnUrl));
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var location = challenge.Headers.Location!;
        Assert.StartsWith(
            FakeGoogle.AuthorizationEndpoint + "?",
            location.ToString(),
            StringComparison.Ordinal);
        var query = Query(location);

        var code = Google.Issue(
            email,
            emailVerified,
            subject,
            query["nonce"],
            issuer: issuer);
        var callback = new Uri(query["redirect_uri"]);
        return await SendAsync(
            HttpMethod.Post,
            callback.AbsolutePath,
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["code"] = code,
                    ["state"] = query["state"],
                }));
    }

    /// <summary>Makes an API token with the token command line, as
    /// the owner would on the server.</summary>
    public async Task<string> CreateTokenAsync(string name)
    {
        _ = Services;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Nudge"] =
                        $"Data Source={Factory.DatabasePath}",
                })
            .Build();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await TokenCommand.RunAsync(
            ["create", name],
            configuration,
            output,
            error,
            TimeProvider.System);
        Assert.Equal(0, exit);
        return output.ToString().Trim();
    }

    /// <summary>
    /// Calls the API with a token made for the test, to set up data
    /// or to read an item as the API shows it. Returns the status
    /// and the body, parsed when it is JSON.
    /// </summary>
    public async Task<(HttpStatusCode Status, JsonNode? Body)> ApiAsync(
        string method,
        string path,
        string? json = null)
    {
        _token ??= await CreateTokenAsync("ui-tests");
        var reply = await SendAsync(
            new HttpMethod(method),
            path,
            json is null
                ? null
                : new StringContent(json, Encoding.UTF8, "application/json"),
            withCookies: false,
            authorization: $"Bearer {_token}");
        var text = await reply.Content.ReadAsStringAsync();
        return (
            reply.StatusCode,
            text.Length == 0 ? null : JsonNode.Parse(text));
    }

    /// <summary>Makes an item through the API and returns its id.
    /// </summary>
    public async Task<Guid> CreateItemAsync(string json)
    {
        var (status, body) = await ApiAsync("POST", "/api/items", json);
        Assert.True(status == HttpStatusCode.Created, body?.ToJsonString());
        return Guid.Parse(body!["data"]!["id"]!.GetValue<string>());
    }

    /// <summary>Makes a project or a context through the API and
    /// returns its id.</summary>
    public async Task<Guid> CreateAsync(
        string path,
        string name)
    {
        var (status, body) = await ApiAsync(
            "POST",
            path,
            JsonSerializer.Serialize(new { name }));
        Assert.True(status == HttpStatusCode.Created, body?.ToJsonString());
        return Guid.Parse(body!["data"]!["id"]!.GetValue<string>());
    }

    /// <summary>The item as the API shows it, as JSON text; null if
    /// it is not there (or deleted).</summary>
    public async Task<string?> ItemJsonAsync(Guid id)
    {
        var (status, body) = await ApiAsync("GET", $"/api/items/{id}");
        return status == HttpStatusCode.NotFound ? null : body!.ToJsonString();
    }

    /// <summary>The newest change in the log: its number, source and
    /// description; null if there is none.</summary>
    public (long Id, string Source, string Description)? LastChange()
    {
        using var connection = new SqliteConnection(
            $"Data Source={Factory.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, Source, Description FROM Changes"
            + " ORDER BY Id DESC LIMIT 1;";
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? (reader.GetInt64(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    /// <summary>The method and path the newest idempotency record
    /// was kept under; null if there is none.</summary>
    public (string Method, string Path)? LastRecord()
    {
        using var connection = new SqliteConnection(
            $"Data Source={Factory.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Method, Path FROM IdempotencyRecords"
            + " ORDER BY rowid DESC LIMIT 1;";
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? (reader.GetString(0), reader.GetString(1))
            : null;
    }

    /// <summary>How many rows a table has.</summary>
    public long Count(string table)
    {
        using var connection = new SqliteConnection(
            $"Data Source={Factory.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>The query of an address, decoded.</summary>
    public static Dictionary<string, string> Query(Uri uri)
    {
        return uri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => Uri.UnescapeDataString(
                    pair.Length > 1 ? pair[1].Replace('+', ' ') : ""));
    }

    /// <summary>The antiforgery token in a page's forms.</summary>
    public static string AntiforgeryToken(string html)
    {
        var match = AntiforgeryInput().Match(html);
        Assert.True(match.Success, "The page has no antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    [GeneratedRegex(
        "name=\"__RequestVerificationToken\"\\s+value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryInput();

    private void Keep(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return;
        }

        foreach (var header in values)
        {
            var pair = header.Split(';', 2)[0].Split('=', 2);
            var name = pair[0].Trim();
            var value = pair.Length > 1 ? pair[1].Trim() : "";
            var removed = value.Length == 0
                || header.Contains(
                    "expires=Thu, 01 Jan 1970",
                    StringComparison.OrdinalIgnoreCase);
            if (removed)
            {
                Cookies.Remove(name);
            }
            else
            {
                Cookies[name] = value;
            }
        }
    }
}
