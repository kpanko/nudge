using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Nudge.Web.Tests.Ui;

/// <summary>
/// Stands in for Google, so that the app's real OpenID Connect
/// handler can be driven end to end without the network: the app is
/// told this issuer's endpoints and signing key instead of Google's,
/// and its calls to the token endpoint come here. A test asks for a
/// code for the account it wants to sign in as, and the token
/// endpoint gives back an id token for that account, signed with a
/// key made for the test.
/// </summary>
internal sealed class FakeGoogle : HttpMessageHandler
{
    public const string Issuer = "https://accounts.example.test";
    public const string AuthorizationEndpoint = Issuer + "/o/oauth2/auth";
    public const string TokenEndpoint = Issuer + "/token";

    private readonly RsaSecurityKey _key =
        new(RSA.Create(2048)) { KeyId = "fake-google" };

    private readonly ConcurrentDictionary<string, string> _codes = new();

    /// <summary>Points the app's Google scheme here.</summary>
    public void Configure(OpenIdConnectOptions options)
    {
        var configuration = new OpenIdConnectConfiguration
        {
            Issuer = Issuer,
            AuthorizationEndpoint = AuthorizationEndpoint,
            TokenEndpoint = TokenEndpoint,
        };
        configuration.SigningKeys.Add(_key);
        options.Configuration = configuration;
        options.BackchannelHttpHandler = this;
    }

    /// <summary>
    /// A code that the token endpoint exchanges for an id token
    /// with these claims; a null email leaves the claim out.
    /// </summary>
    public string Issue(
        string? email,
        bool emailVerified,
        string subject,
        string nonce,
        string audience = WebUiHost.ClientId,
        string issuer = Issuer)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = subject,
            ["email_verified"] = emailVerified,
            ["nonce"] = nonce,
        };
        if (email is not null)
        {
            claims["email"] = email;
        }

        var now = DateTime.UtcNow;
        var token = new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = issuer,
                Audience = audience,
                Claims = claims,
                IssuedAt = now,
                NotBefore = now,
                Expires = now.AddMinutes(5),
                SigningCredentials = new SigningCredentials(
                    _key,
                    SecurityAlgorithms.RsaSha256),
            });
        var code = Guid.NewGuid().ToString("N");
        _codes[code] = token;
        return code;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri?.ToString() != TokenEndpoint
            || request.Content is null)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var form = await request.Content.ReadAsStringAsync(
            cancellationToken);
        var code = form
            .Split('&')
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair[0] == "code")
            .Select(pair => Uri.UnescapeDataString(pair[1]))
            .SingleOrDefault();
        if (code is null || !_codes.TryRemove(code, out var idToken))
        {
            return Json(
                HttpStatusCode.BadRequest,
                new { error = "invalid_grant" });
        }

        return Json(
            HttpStatusCode.OK,
            new
            {
                access_token = "fake-access-token",
                token_type = "Bearer",
                expires_in = 3600,
                id_token = idToken,
            });
    }

    private static HttpResponseMessage Json(
        HttpStatusCode status,
        object body)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"),
        };
    }
}
