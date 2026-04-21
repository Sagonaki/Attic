namespace Attic.Api.RateLimiting;

public static class RateLimitPolicyNames
{
    public const string AuthFixed = "auth-fixed-5-per-min";
    public const string UploadFixed = "upload-fixed-60-per-min";
}
