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

    public static CookieOptions CreateSessionCookieOptions(DateTimeOffset expiresAt) => new()
    {
        HttpOnly = true,
        Secure = false,  // Phase 1: HTTP dev environment; set to true for HTTPS in production
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expiresAt
    };
}
