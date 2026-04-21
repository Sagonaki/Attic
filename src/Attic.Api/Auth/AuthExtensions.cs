using Microsoft.Extensions.DependencyInjection;

namespace Attic.Api.Auth;

public static class AuthExtensions
{
    public static IServiceCollection AddAtticAuth(this IServiceCollection services)
    {
        services.AddScoped<CurrentUser>();
        services.AddScoped<SessionFactory>();
        services
            .AddAuthentication(AtticAuthenticationOptions.Scheme)
            .AddScheme<AtticAuthenticationOptions, AtticAuthenticationHandler>(AtticAuthenticationOptions.Scheme, _ => { });
        services.AddAuthorization();
        return services;
    }

    public static CookieOptions CreateSessionCookieOptions(HttpRequest request, DateTimeOffset expiresAt) => new()
    {
        HttpOnly = true,
        Secure = request.IsHttps,   // tracks the request scheme: false for dev HTTP, true for prod HTTPS
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expiresAt
    };
}
