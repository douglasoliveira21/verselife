using System.Net;

namespace LifeVerse.Scripting;

/// <summary>The rules for an address a script may make the players listen to: the same as the music of a parcel (HTTPS, no login in the address, no machine inside a network, an mp3 or ogg file). Null when fine, otherwise why not.</summary>
public static class MediaUrl
{
    public static string? Check(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "not_https";
        if (url.Length > 512) return "too_long";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0) return "not_https";
        var host = uri.Host.ToLowerInvariant().Trim('[', ']');
        var isAddress = IPAddress.TryParse(host, out var ip);
        if (host.Length is 0 or > 255 || (!host.Contains('.') && !isAddress && !host.Contains(':'))) return "bad_host";
        if (host is "localhost" or "postgres" or "redis" or "minio" || host.EndsWith(".internal", StringComparison.Ordinal) || host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".localhost", StringComparison.Ordinal)) return "bad_host";
        if (isAddress && PrivateAddress(ip!)) return "bad_host";
        var path = uri.AbsolutePath.ToLowerInvariant();
        return path.EndsWith(".mp3", StringComparison.Ordinal) || path.EndsWith(".ogg", StringComparison.Ordinal) ? null : "bad_type";
    }

    private static bool PrivateAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;
        var bytes = ip.GetAddressBytes();
        if (bytes.Length == 16) return (bytes[0] & 0xFE) == 0xFC;
        return bytes[0] == 10 || bytes[0] == 127 || bytes[0] == 0 || (bytes[0] == 169 && bytes[1] == 254) || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127);
    }
}
