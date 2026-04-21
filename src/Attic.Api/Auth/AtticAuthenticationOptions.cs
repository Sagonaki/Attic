using Microsoft.AspNetCore.Authentication;

namespace Attic.Api.Auth;

public sealed class AtticAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string Scheme = "AtticCookie";
    public const string CookieName = "attic.session";
}
