using LifeVerse.IslandServer.Scripts;
using LifeVerse.Scripting;
using LifeVerse.Shared.Terrain;

namespace LifeVerse.IslandServer.Core;

/// <summary>Where a simulated body is: position, rotation (a quaternion, in the same numbers the game uses) and whether it is at rest.</summary>
public readonly record struct BodyPose(Guid Id, float X, float Y, float Z, float QX, float QY, float QZ, float QW, bool Asleep);

/// <summary>
/// The physics of the island's objects, behind an interface so the core stays free of engines. An object marked "physical" becomes a rigid body (gravity,
/// collisions, rolling, bouncing); everything else solid stands in as a fixed body and the terrain is the ground. Only the server simulates; the poses of
/// the moving bodies reach the players a few times per second (the "physics" operation). The Unity Dedicated Server brings the PhysX implementation; the
/// plain .NET host has none (objects simply do not fall there).
/// </summary>
public interface IIslandPhysics : IDisposable
{
    int BodyCount { get; }
    int FixedCount { get; }
    int MaxBodies { get; }

    /// <summary>The terrain is the ground; the core calls this again when it has been sculpted (the revision changed).</summary>
    void SetTerrain(TerrainMap terrain);

    /// <summary>Reads the objects again: new physical objects become bodies, moved ones are put back where the builder left them, removed ones go away.</summary>
    void Sync(IEnumerable<PhysicsBodyDefinition> definitions);

    /// <summary>Advances the simulation by <paramref name="seconds"/>.</summary>
    void Step(float seconds);

    IReadOnlyList<BodyPose> Poses();

    /// <summary>A walking avatar shoves the bodies it touches: a ball is kicked away from the feet and goes with the speed of the walker.</summary>
    void Push(float feetX, float feetY, float feetZ, float velocityX, float velocityZ);

    /// <summary>"impulse", "velocity", "angular" or "gravity" (gravity scale in X). False for an unknown action or an object that is no body.</summary>
    bool Apply(Guid id, string action, Vec3 value);

    Vec3? VelocityOf(Guid id);

    /// <summary>A body touched (or left) something: the object, what it touched (another object, or null for the ground), whether it started and how fast it was going.</summary>
    Action<Guid, Guid?, bool, float>? Contacted { get; set; }
}
