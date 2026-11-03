using System.Numerics;
using System.Text.Json;

namespace LifeVerse.Shared.Building;

/// <summary>One object inside a saved construction. Positions are relative to the construction's base point; <see cref="Group"/> is an index into the construction's own groups.</summary>
public sealed record PackageObject(
    string PrimitiveType, string Name, string Description,
    float X, float Y, float Z, float RotationX, float RotationY, float RotationZ, float ScaleX, float ScaleY, float ScaleZ,
    string MaterialJson, string GeometryJson, string PhysicsJson, string ContentJson, int? Group);

/// <summary>A construction saved in the inventory: a house, a sofa, a shop. It is plain data, so it can be validated before it is stored or placed.</summary>
public sealed record BuildPackage(string Format, int Version, string Name, float BaseOffset, PackageObject[] Objects);

/// <summary>An object of the world as the package builder sees it (absolute position, real group id).</summary>
public sealed record SourceObject(
    string PrimitiveType, string Name, string Description,
    float X, float Y, float Z, float RotationX, float RotationY, float RotationZ, float ScaleX, float ScaleY, float ScaleZ,
    string MaterialJson, string GeometryJson, string PhysicsJson, string ContentJson, Guid? GroupId);

/// <summary>An object ready to be created in the world: absolute position and the group id it must share with its siblings.</summary>
public sealed record PlacedObject(PackageObject Source, Vector3 Position, Guid? GroupId);

public static class BuildPackages
{
    public const string FormatName = "lifeverse.build";
    public const int CurrentVersion = 1;
    public const int MaxObjects = 500;
    public const int MaxBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>
    /// Builds a package from objects in the world. The base point is the middle of the construction on the ground plane and the
    /// height of its lowest object, so placing it puts that object's underside on the ground.
    /// </summary>
    public static BuildPackage Create(string name, IReadOnlyList<SourceObject> sources)
    {
        if (sources.Count == 0) throw new ArgumentException("Nothing to save.", nameof(sources));
        if (sources.Count > MaxObjects) throw new ArgumentException($"A construction can have at most {MaxObjects} objects.", nameof(sources));
        var centerX = (sources.Min(s => s.X) + sources.Max(s => s.X)) / 2f; var centerZ = (sources.Min(s => s.Z) + sources.Max(s => s.Z)) / 2f;
        var lowest = sources.OrderBy(s => s.Y - s.ScaleY / 2f).First();
        var baseY = lowest.Y;
        var groups = sources.Where(s => s.GroupId is not null).Select(s => s.GroupId!.Value).Distinct().ToList();
        var objects = sources.Select(s => new PackageObject(s.PrimitiveType, s.Name, s.Description, s.X - centerX, s.Y - baseY, s.Z - centerZ,
            s.RotationX, s.RotationY, s.RotationZ, s.ScaleX, s.ScaleY, s.ScaleZ, s.MaterialJson, s.GeometryJson, s.PhysicsJson, s.ContentJson,
            s.GroupId is { } id ? groups.IndexOf(id) : null)).ToArray();
        return new BuildPackage(FormatName, CurrentVersion, Clean(name), lowest.ScaleY / 2f, objects);
    }

    private static string Clean(string name) { name = (name ?? string.Empty).Trim(); return name.Length == 0 ? "Construção" : name[..Math.Min(name.Length, 160)]; }

    public static string Serialize(BuildPackage package) => JsonSerializer.Serialize(package, Options);

    /// <summary>Reads and checks a package. Returns null (and says why) for anything that is not a well-formed construction.</summary>
    public static BuildPackage? Parse(string json, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxBytes) { error = "The construction is empty or too large."; return null; }
        BuildPackage? package;
        try { package = JsonSerializer.Deserialize<BuildPackage>(json, Options); }
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
        }
        return package;
    }

    /// <summary>The objects of a package at a place in the world: <paramref name="ground"/> is where the construction's base stands. Each package group becomes a new group id.</summary>
    public static IReadOnlyList<PlacedObject> Place(BuildPackage package, Vector3 ground, Func<Guid>? newId = null)
    {
        newId ??= Guid.NewGuid;
        var groups = new Dictionary<int, Guid>();
        return package.Objects.Select(o =>
        {
            Guid? group = null;
            if (o.Group is { } index) { if (!groups.TryGetValue(index, out var id)) groups[index] = id = newId(); group = id; }
            return new PlacedObject(o, new Vector3(ground.X + o.X, ground.Y + package.BaseOffset + o.Y, ground.Z + o.Z), group);
        }).ToArray();
    }
}
