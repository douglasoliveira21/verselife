namespace LifeVerse.Api.Infrastructure;

/// <summary>The limits of sign-up, sign-in and session refresh, per client address and per minute. They come from the configuration (RateLimits:AuthPerMinute, RateLimits:RefreshPerMinute); a missing, zero or negative value is ignored.</summary>
public static class RateLimitSettings
{
    public const int DefaultAuthPerMinute = 30, DefaultRefreshPerMinute = 120, MaxPerMinute = 10_000;
    public static int AuthPerMinute(IConfiguration configuration) => Read(configuration, "RateLimits:AuthPerMinute", DefaultAuthPerMinute);
    public static int RefreshPerMinute(IConfiguration configuration) => Read(configuration, "RateLimits:RefreshPerMinute", DefaultRefreshPerMinute);
    internal static int Read(IConfiguration configuration, string key, int fallback) => int.TryParse(configuration[key], out var value) && value is > 0 and <= MaxPerMinute ? value : fallback;
}
