using System.Text.Json;

namespace LifeVerse.Api.Modules.Inventory;

/// <summary>An object inside a saved construction (positions relative to the construction's base point).</summary>
public sealed record PackageObjectDto(
    string PrimitiveType, string Name, string Description,
    float X, float Y, float Z, float RotationX, float RotationY, float RotationZ, float ScaleX, float ScaleY, float ScaleZ,
    string MaterialJson, string GeometryJson, string PhysicsJson, string ContentJson, int? Group, int? Parent = null, float PivotX = 0, float PivotY = 0, float PivotZ = 0);

/// <summary>An object of the world as the package builder reads it (absolute position, real group id).</summary>
public sealed record PackageSourceDto(
    string PrimitiveType, string Name, string Description,
    float X, float Y, float Z, float RotationX, float RotationY, float RotationZ, float ScaleX, float ScaleY, float ScaleZ,
    string MaterialJson, string GeometryJson, string PhysicsJson, string ContentJson, Guid? GroupId, Guid Id = default, Guid? ParentId = null, float PivotX = 0, float PivotY = 0, float PivotZ = 0);

public sealed record PackageDto(string Format, int Version, string Name, float BaseOffset, PackageObjectDto[] Objects);

/// <summary>
/// The saved-construction format as the API reads it. The client writes it with <c>LifeVerse.Shared.Building.BuildPackages</c>; the API does
/// not reference that project (its container only holds the API), so the format rules live here too and a test keeps both in step.
/// </summary>
public static class PackageDocument
{
    public const string FormatName = "lifeverse.build";
    public const int CurrentVersion = 1;
    public const int MaxObjects = 500;
    public const int MaxBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Builds a package from objects of the world, exactly as the client does (a test compares the two).</summary>
    public static PackageDto Create(string name, IReadOnlyList<PackageSourceDto> sources)
    {
        if (sources.Count == 0 || sources.Count > MaxObjects) throw new ArgumentException("A construction needs 1 to 500 objects.", nameof(sources));
        var centerX = (sources.Min(s => s.X) + sources.Max(s => s.X)) / 2f; var centerZ = (sources.Min(s => s.Z) + sources.Max(s => s.Z)) / 2f;
        var lowest = sources.OrderBy(s => s.Y - s.ScaleY / 2f).First();
        var groups = sources.Where(s => s.GroupId is not null).Select(s => s.GroupId!.Value).Distinct().ToList();
        var objects = sources.Select(s => new PackageObjectDto(s.PrimitiveType, s.Name, s.Description, s.X - centerX, s.Y - lowest.Y, s.Z - centerZ,
            s.RotationX, s.RotationY, s.RotationZ, s.ScaleX, s.ScaleY, s.ScaleZ, s.MaterialJson, s.GeometryJson, s.PhysicsJson, s.ContentJson,
            s.GroupId is { } id ? groups.IndexOf(id) : null, ParentIndex(sources, s), s.PivotX, s.PivotY, s.PivotZ)).ToArray();
        name = (name ?? string.Empty).Trim();
        return new PackageDto(FormatName, CurrentVersion, name.Length == 0 ? "Construção" : name[..Math.Min(name.Length, 160)], lowest.ScaleY / 2f, objects);
    }

    private static int? ParentIndex(IReadOnlyList<PackageSourceDto> sources, PackageSourceDto s)
    {
        if (s.ParentId is not { } parent || s.GroupId is null) return null;
        for (var i = 0; i < sources.Count; i++) if (sources[i].Id == parent && sources[i].Id != Guid.Empty && sources[i].GroupId == s.GroupId) return i;
        return null;
    }

    /// <summary>Null when every parent exists, is in the same group and the hierarchy has no loop; otherwise why not.</summary>
    public static string? HierarchyError(IReadOnlyList<(int? Group, int? Parent)> objects)
    {
        for (var i = 0; i < objects.Count; i++)
        {
            if (objects[i].Parent is not { } parent) continue;
            if (parent < 0 || parent >= objects.Count || parent == i) return "An object hangs from a parent that does not exist.";
            if (objects[i].Group is null || objects[i].Group != objects[parent].Group) return "An object hangs from a parent that is not in its group.";
            var hops = 0; var at = (int?)parent;
            while (at is { } step) { if (++hops > objects.Count) return "The hierarchy of the construction has a loop."; at = objects[step].Parent is { } up && up >= 0 && up < objects.Count ? up : null; }
        }
        return null;
    }

    public static string Serialize(PackageDto package) => JsonSerializer.Serialize(package, Options);

    public static PackageDto? Parse(string json, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxBytes) { error = "The construction is empty or too large."; return null; }
        PackageDto? package;
        try { package = JsonSerializer.Deserialize<PackageDto>(json, Options); }
        catch (JsonException) { error = "The construction is not valid JSON."; return null; }
        if (package is null || package.Format != FormatName || package.Version != CurrentVersion) { error = "This is not a LifeVerse construction (or it is from a newer version)."; return null; }
        if (package.Objects is null || package.Objects.Length is 0 or > MaxObjects) { error = $"A construction needs 1 to {MaxObjects} objects."; return null; }
        if ((package.Name?.Length ?? 0) is 0 or > 160 || !float.IsFinite(package.BaseOffset) || package.BaseOffset is < 0 or > 64) { error = "The construction name or base is invalid."; return null; }
        var groupCount = package.Objects.Count(o => o.Group is not null) == 0 ? 0 : package.Objects.Max(o => o.Group ?? -1) + 1;
        foreach (var o in package.Objects)
        {
            if (string.IsNullOrWhiteSpace(o.PrimitiveType)) { error = "An object has no shape."; return null; }
            var numbers = new[] { o.X, o.Y, o.Z, o.RotationX, o.RotationY, o.RotationZ, o.ScaleX, o.ScaleY, o.ScaleZ };
            if (numbers.Any(n => !float.IsFinite(n)) || Math.Abs(o.X) > 2000 || Math.Abs(o.Y) > 2000 || Math.Abs(o.Z) > 2000) { error = "An object has invalid numbers."; return null; }
            if (new[] { o.ScaleX, o.ScaleY, o.ScaleZ }.Any(s => s is < .01f or > 64f)) { error = "An object has a size outside 0.01 to 64 metres."; return null; }
            if (o.Group is < 0 || o.Group >= Math.Max(groupCount, 1) && o.Group is not null) { error = "An object points to a group that does not exist."; return null; }
            if (new[] { o.PivotX, o.PivotY, o.PivotZ }.Any(p => !float.IsFinite(p) || Math.Abs(p) > 64f)) { error = "An object has a pivot outside 64 metres."; return null; }
        }
        if (HierarchyError(package.Objects.Select(o => (o.Group, o.Parent)).ToArray()) is { } hierarchy) { error = hierarchy; return null; }
        return package;
    }
}
