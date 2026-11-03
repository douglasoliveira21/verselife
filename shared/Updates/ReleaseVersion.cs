namespace LifeVerse.Shared.Updates;

/// A "major.minor.patch" version with an optional "-suffix" (ignored when comparing). Anything else does not parse,
/// so a broken release setting can never be read as "everything is out of date".
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch) : IComparable<ReleaseVersion>
{
    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var core = text.Trim().TrimStart('v', 'V').Split('-', '+')[0].Split('.');
        if (core.Length is < 2 or > 3) return false;
        if (!int.TryParse(core[0], out var major) || !int.TryParse(core[1], out var minor)) return false;
        var patch = 0;
        if (core.Length == 3 && !int.TryParse(core[2], out patch)) return false;
        if (major < 0 || minor < 0 || patch < 0) return false;
        version = new ReleaseVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(ReleaseVersion other) => (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

public enum UpdateStatus { UpToDate, Available, Required, Unknown }

public static class UpdateRules
{
    /// Required when the client is older than the minimum, available when older than the latest, otherwise up to date.
    /// Unparsable input never forces an update.
    public static UpdateStatus Evaluate(string? current, string? latest, string? minimum)
    {
        if (!ReleaseVersion.TryParse(current, out var mine)) return UpdateStatus.Unknown;
        if (ReleaseVersion.TryParse(minimum, out var min) && mine.CompareTo(min) < 0) return UpdateStatus.Required;
        if (ReleaseVersion.TryParse(latest, out var newest) && mine.CompareTo(newest) < 0) return UpdateStatus.Available;
        return UpdateStatus.UpToDate;
    }
}

public sealed record ReleaseInfo(string Latest, string Minimum, string DownloadUrl, string Notes)
{
    /// Reads the API answer (camelCase). Broken, partial or oversized answers give null, which the client treats as "no news".
    public static ReleaseInfo? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 8192) return null;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            string Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
            var info = new ReleaseInfo(Text("latest"), Text("minimum"), Text("downloadUrl"), Text("notes"));
            return info.Latest.Length == 0 && info.Minimum.Length == 0 ? null : info;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// Links are only ever opened when they are plain http(s) addresses.
    public bool HasSafeDownloadLink => Uri.TryCreate(DownloadUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
