namespace LifeVerse.Api.Modules.Platform;

/// What the desktop client may know about new versions. Set in configuration (ClientRelease:*) when a build is published;
/// the client decides what to do by comparing its own version with these two (see shared UpdateRules).
public sealed class ClientReleaseOptions
{
    public string Latest { get; init; } = string.Empty;
    public string Minimum { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string DownloadUrlWindows { get; init; } = string.Empty;
    public string DownloadUrlLinux { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
}

public sealed record ClientReleaseInfo(string Latest, string Minimum, string DownloadUrl, string Notes);

public static class ClientReleaseEndpoints
{
    public static IEndpointRouteBuilder MapClientReleaseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/client/release", (string? platform, ClientReleaseOptions options) => Results.Ok(Describe(options, platform)))
            .WithName("GetClientRelease").WithTags("Platform").AllowAnonymous();
        return endpoints;
    }

    /// The platform-specific link when there is one, the general link otherwise. Only http(s) links are ever handed out,
    /// so a typo in the setting can't make the client open something else.
    public static ClientReleaseInfo Describe(ClientReleaseOptions options, string? platform)
    {
        var specific = (platform ?? string.Empty).Trim().ToLowerInvariant() switch { "windows" => options.DownloadUrlWindows, "linux" => options.DownloadUrlLinux, _ => string.Empty };
        var url = IsWebLink(specific) ? specific : options.DownloadUrl;
        return new ClientReleaseInfo(options.Latest.Trim(), options.Minimum.Trim(), IsWebLink(url) ? url : string.Empty, options.Notes.Length > 1000 ? options.Notes[..1000] : options.Notes);
    }

    private static bool IsWebLink(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
