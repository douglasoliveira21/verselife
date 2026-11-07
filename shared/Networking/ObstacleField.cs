using System.Numerics;
using System.Text.Json;

namespace LifeVerse.Shared.Networking;

public enum ObstacleShape { Box, Cylinder, Sphere }

/// <summary>A solid thing in the world as the movement simulation sees it: a box, an upright cylinder or a sphere, placed and turned.</summary>
public readonly record struct Obstacle(ObstacleShape Shape, Vector3 Center, Quaternion Rotation, Vector3 Half, float Radius)
{
    public float Reach => Shape == ObstacleShape.Sphere ? Radius : MathF.Sqrt(Half.X * Half.X + Half.Y * Half.Y + Half.Z * Half.Z);
}

/// <summary>
/// What stops a walking avatar. The island server simulates every player's movement (terrain only, until now) and the clients move with the
/// engine's physics, so when the simulation knew nothing of the objects the two disagreed: a player pushing against a wall was pulled through
/// it, and one standing on a box was pulled down into it every snapshot (the camera shook). The field gives the simulation the same walls,
/// floors and steps the player feels, using simple shapes: exact for boxes, upright cylinders and spheres, a close bound for the rest.
/// </summary>
public sealed class ObstacleField
{
    public const float Radius = .45f;      // the avatar's body: the same capsule as the player scene (radius 0.45, height 1.8)
    public const float Height = 1.8f;
    public const float StepUp = .4f;       // the highest ledge walked onto without jumping

    private readonly Dictionary<Guid, Obstacle> _items = [];
    public int Count => _items.Count;

    /// <summary>True for objects that stop people: solid, not a trigger zone.</summary>
    public static bool Blocks(string? physicsJson)
    {
        if (string.IsNullOrWhiteSpace(physicsJson)) return true;
        try
        {
            using var document = JsonDocument.Parse(physicsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return true;
            if (document.RootElement.TryGetProperty("collision", out var collision) && collision.ValueKind == JsonValueKind.False) return false;
            if (document.RootElement.TryGetProperty("trigger", out var trigger) && trigger.ValueKind == JsonValueKind.True) return false;
            if (document.RootElement.TryGetProperty("shape", out var shape) && shape.ValueKind == JsonValueKind.String && shape.GetString() == "none") return false;
        }
        catch (JsonException) { }
        return true;
    }

    /// <summary>The simple shape standing in for an object (position and Euler angles in degrees, scale as in the world).</summary>
    public static Obstacle? For(string primitiveType, Vector3 position, Vector3 degrees, Vector3 scale)
    {
        // Godot turns Euler angles in the order Y, X, Z, which is what yaw/pitch/roll means here.
        var rotation = Quaternion.CreateFromYawPitchRoll(degrees.Y * MathF.PI / 180f, degrees.X * MathF.PI / 180f, degrees.Z * MathF.PI / 180f);
        var type = (primitiveType ?? "cube").ToLowerInvariant();
        var upright = MathF.Abs(Vector3.Transform(Vector3.UnitY, rotation).Y) > .995f;
        var wide = .5f * MathF.Max(scale.X, scale.Z);
        return type switch
        {
            "sphere" => new Obstacle(ObstacleShape.Sphere, position, rotation, Vector3.Zero, .5f * MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z))),
            "cylinder" or "cone" or "tube" => upright
                ? new Obstacle(ObstacleShape.Cylinder, position, rotation, new Vector3(wide, .5f * scale.Y, wide), wide)
                : new Obstacle(ObstacleShape.Box, position, rotation, new Vector3(wide, .5f * scale.Y, wide), 0),
            "torus" or "ring" => new Obstacle(ObstacleShape.Cylinder, position, rotation, new Vector3(wide, .15f * scale.Y, wide), wide),
            "plane" => new Obstacle(ObstacleShape.Box, position, rotation, new Vector3(.5f * scale.X, .025f * scale.Y, .5f * scale.Z), 0),
            "hemisphere" => new Obstacle(ObstacleShape.Box, position, rotation, new Vector3(.5f * scale.X, .25f * scale.Y, .5f * scale.Z), 0),
            _ => new Obstacle(ObstacleShape.Box, position, rotation, scale * .5f, 0)
        };
    }

    public void Set(Guid id, Obstacle? obstacle) { if (obstacle is { } o) _items[id] = o; else _items.Remove(id); }
    public void Remove(Guid id) => _items.Remove(id);
    public void Clear() => _items.Clear();

    // ---- pushing the avatar out of things ---------------------------------------------------------------------

    /// <summary>
    /// Moves the avatar out of anything it is inside of, and takes away the part of its speed that pushes into it. Ledges lower than
    /// <see cref="StepUp"/> above the feet are not walls (the avatar steps onto them); a ceiling stops a jump.
    /// </summary>
    public void Resolve(AuthoritativePlayerState state)
    {
        if (_items.Count == 0) return;
        Span<float> offsets = [Radius, Height / 2f, Height - Radius];
        for (var pass = 0; pass < 2; pass++)
        {
            var pushed = false;
            foreach (var obstacle in _items.Values)
            {
                var dx = state.X - obstacle.Center.X; var dz = state.Z - obstacle.Center.Z;
                var reach = obstacle.Reach + Radius + 1f;
                if (dx * dx + dz * dz > reach * reach) continue;
                foreach (var offset in offsets)
                {
                    var center = new Vector3(state.X, state.Y + offset, state.Z);
                    if (!Contact(obstacle, center, out var normal, out var depth, out var contactHeight)) continue;
                    if (contactHeight <= state.Y + StepUp) continue; // a ledge to step onto, not a wall
                    state.X += normal.X * depth; state.Z += normal.Z * depth;
                    if (normal.Y < -.3f) { state.Y += normal.Y * depth; if (state.VelocityY > 0) state.VelocityY = 0; } // a ceiling
                    var into = state.VelocityX * normal.X + state.VelocityZ * normal.Z;
                    if (into < 0) { state.VelocityX -= normal.X * into; state.VelocityZ -= normal.Z * into; }
                    pushed = true;
                }
            }
            if (!pushed) break;
        }
    }

    /// <summary>Does a sphere of the avatar's radius touch the obstacle? If so, which way to push it out, how far, and the height of the touching point.</summary>
    private static bool Contact(Obstacle o, Vector3 sphere, out Vector3 normal, out float depth, out float contactHeight)
    {
        normal = Vector3.UnitY; depth = 0; contactHeight = 0;
        var inverse = Quaternion.Inverse(o.Rotation);
        var local = Vector3.Transform(sphere - o.Center, inverse);
        Vector3 closest, away;
        switch (o.Shape)
        {
            case ObstacleShape.Sphere:
            {
                var length = local.Length();
                if (length >= o.Radius + Radius) return false;
                closest = length < 1e-5f ? Vector3.Zero : local / length * MathF.Min(length, o.Radius);
                away = length < 1e-5f ? Vector3.UnitY : local / length;
                depth = o.Radius + Radius - length;
                break;
            }
            case ObstacleShape.Cylinder:
            {
                var radial = MathF.Sqrt(local.X * local.X + local.Z * local.Z);
                var cy = Math.Clamp(local.Y, -o.Half.Y, o.Half.Y); var scale = radial > o.Radius ? o.Radius / radial : 1f;
                closest = new Vector3(local.X * scale, cy, local.Z * scale);
                var delta = local - closest; var distance = delta.Length();
                if (distance >= Radius) return false;
                if (distance > 1e-5f) { away = delta / distance; depth = Radius - distance; }
                else
                {
                    var penRadial = o.Radius - radial; var penTop = o.Half.Y - MathF.Abs(local.Y);
                    if (penTop < penRadial) { away = new Vector3(0, local.Y >= 0 ? 1 : -1, 0); depth = penTop + Radius; closest = new Vector3(local.X, local.Y >= 0 ? o.Half.Y : -o.Half.Y, local.Z); }
                    else { away = radial < 1e-5f ? Vector3.UnitX : new Vector3(local.X / radial, 0, local.Z / radial); depth = penRadial + Radius; closest = new Vector3(away.X * o.Radius, local.Y, away.Z * o.Radius); }
                }
                break;
            }
            default:
            {
                closest = Vector3.Clamp(local, -o.Half, o.Half);
                var delta = local - closest; var distance = delta.Length();
                if (distance >= Radius) return false;
                if (distance > 1e-5f) { away = delta / distance; depth = Radius - distance; }
                else
                {
                    var pen = o.Half - Vector3.Abs(local);
                    if (pen.X <= pen.Y && pen.X <= pen.Z) { away = new Vector3(local.X >= 0 ? 1 : -1, 0, 0); depth = pen.X + Radius; closest = new Vector3(local.X >= 0 ? o.Half.X : -o.Half.X, local.Y, local.Z); }
                    else if (pen.Y <= pen.Z) { away = new Vector3(0, local.Y >= 0 ? 1 : -1, 0); depth = pen.Y + Radius; closest = new Vector3(local.X, local.Y >= 0 ? o.Half.Y : -o.Half.Y, local.Z); }
                    else { away = new Vector3(0, 0, local.Z >= 0 ? 1 : -1); depth = pen.Z + Radius; closest = new Vector3(local.X, local.Y, local.Z >= 0 ? o.Half.Z : -o.Half.Z); }
                }
                break;
            }
        }
        normal = Vector3.Normalize(Vector3.Transform(away, o.Rotation));
        contactHeight = (o.Center + Vector3.Transform(closest, o.Rotation)).Y;
        return true;
    }

    // ---- standing on things -------------------------------------------------------------------------------------

    /// <summary>
    /// The height of the highest upward-facing surface under the avatar that it can stand on: not more than <see cref="StepUp"/> above
    /// <paramref name="feetY"/>. Negative infinity when there is none. Spheres are never stood on; the avatar slides off them as it does in the game.
    /// </summary>
    public float SupportHeight(float x, float feetY, float z)
    {
        var best = float.NegativeInfinity;
        foreach (var o in _items.Values)
        {
            if (o.Shape == ObstacleShape.Sphere) continue;
            var dx = x - o.Center.X; var dz = z - o.Center.Z;
            var reach = o.Reach + .1f;
            if (dx * dx + dz * dz > reach * reach) continue;
            var up = Vector3.Transform(Vector3.UnitY, o.Rotation);
            if (up.Y < .7f) continue;
            var local = Vector3.Transform(new Vector3(x, o.Center.Y, z) - o.Center, Quaternion.Inverse(o.Rotation));
            bool inside = o.Shape == ObstacleShape.Cylinder ? local.X * local.X + local.Z * local.Z <= o.Radius * o.Radius : MathF.Abs(local.X) <= o.Half.X && MathF.Abs(local.Z) <= o.Half.Z;
            if (!inside) continue;
            // the top face at this spot, where the rotated box really is
            var top = o.Center.Y + (Vector3.Transform(new Vector3(local.X, o.Half.Y, local.Z), o.Rotation).Y);
            if (top <= feetY + StepUp && top > best) best = top;
        }
        return best;
    }
}
