using System.Net;

namespace LifeVerse.Shared.Land;

/// <summary>
/// Which addresses a parcel may play music from. The player's own computer downloads the file, so the rules are strict: HTTPS only, no
/// login in the address, no machine inside a network (localhost, private ranges, ".local" and ".internal" names), and a file the game can
/// play (mp3 or ogg). The API checks the same rules before saving; the client checks again before it fetches anything.
/// </summary>
public static class LandMediaUrl
{
    public const int MaxLength = 512;

    /// <summary>Null when the address is acceptable, otherwise why not: too_long, not_https, bad_host or bad_type.</summary>
    public static string? Check(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "not_https";
        if (url.Length > MaxLength) return "too_long";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0) return "not_https";
        var host = uri.Host.ToLowerInvariant().Trim('[', ']');
        var isAddress = IPAddress.TryParse(host, out var ip);
        if (host.Length is 0 or > 255 || (!host.Contains('.') && !isAddress && !host.Contains(':'))) return "bad_host";
        if (host is "localhost" or "postgres" or "redis" or "minio" || host.EndsWith(".internal", StringComparison.Ordinal) || host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".localhost", StringComparison.Ordinal)) return "bad_host";
        if (isAddress && Private(ip!)) return "bad_host";
        var path = uri.AbsolutePath.ToLowerInvariant();
        return path.EndsWith(".mp3", StringComparison.Ordinal) || path.EndsWith(".ogg", StringComparison.Ordinal) ? null : "bad_type";
    }

    /// <summary>Loopback, link-local, private and unspecified addresses, in IPv4 and IPv6.</summary>
    public static bool Private(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;
        var bytes = ip.GetAddressBytes();
        if (bytes.Length == 16) return (bytes[0] & 0xFE) == 0xFC; // unique local fc00::/7
        return bytes[0] == 10 || bytes[0] == 127 || bytes[0] == 0 || (bytes[0] == 169 && bytes[1] == 254) || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) || bytes[0] >= 224;
    }
}
