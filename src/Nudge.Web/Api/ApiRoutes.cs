namespace Nudge.Web.Api;

/// <summary>Where the API is put together.</summary>
public static class ApiRoutes
{
    public const string Prefix = "/api";

    public static IServiceCollection AddNudgeApi(
        this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddScoped<WriteRunner>();
        services.AddSingleton<ChangeSource>();
        return services;
    }

    /// <summary>
    /// Maps every endpoint of the API under one route group:
    /// everything under <see cref="Prefix"/> is the API, and nothing
    /// else is. Every endpoint needs an API token, through the
    /// app's fallback policy (see
    /// <see cref="Auth.ApiTokenAuthentication"/>), so none of them
    /// may allow anonymous requests.
    /// </summary>
    public static RouteGroupBuilder MapNudgeApi(
        this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup(Prefix);

        // What every request without an active token is answered.
        api.WithMetadata(
            new ProducesResponseTypeMetadata(
                StatusCodes.Status401Unauthorized,
                typeof(Microsoft.AspNetCore.Mvc.ProblemDetails),
                ["application/problem+json"]));
        ItemEndpoints.Map(api);
        ProjectEndpoints.Map(api);
        ContextEndpoints.Map(api);
        ChangeEndpoints.Map(api);
        return api;
    }
}
