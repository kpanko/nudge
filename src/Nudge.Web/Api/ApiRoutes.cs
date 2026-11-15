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
    /// Maps every endpoint of the API under one route group. This
    /// group is the one place to attach authentication (the API-token
    /// task): everything under <see cref="Prefix"/> is the API, and
    /// nothing else is.
    /// </summary>
    public static RouteGroupBuilder MapNudgeApi(
        this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup(Prefix);
        ItemEndpoints.Map(api);
        ProjectEndpoints.Map(api);
        ContextEndpoints.Map(api);
        ChangeEndpoints.Map(api);
        return api;
    }
}
