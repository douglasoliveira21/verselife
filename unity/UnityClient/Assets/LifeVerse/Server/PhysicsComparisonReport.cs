using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using LifeVerse.IslandServer.Core;
using LifeVerse.IslandServer.Scripts;
using LifeVerse.Scripting;
using LifeVerse.Shared.Terrain;
using UnityEngine;

namespace LifeVerse.Unity.Server
{
    /// <summary>
    /// The same scenarios, parameters and measurements the Godot island physics was measured with (that project is in the git history; the table is in MIGRATION_ISLAND_SERVER.md), run on the
    /// island physics of this server, so the two engines can be compared line by line: "RESULT name key=value …" with the same keys. Flat ground at y = 0 or a slope, 60 Hz,
    /// 1 m balls (radius .5) and boxes unless said. The first five scenarios are the original ones.
    /// </summary>
    public static class PhysicsComparisonReport
    {
        private const float Dt = 1f / 60f;
        public static readonly (string Name, float Friction)[] Frictions = { ("vidro", .2f), ("metal", .3f), ("plastico", .4f), ("carne", .5f), ("pedra", .6f), ("borracha", .9f) };
        public static readonly (string Name, float Bounce)[] Bounces = { ("pedra", .1f), ("metal", .3f), ("madeira", .4f), ("plastico", .5f), ("vidro", .6f), ("borracha", .9f) };
        public static readonly int[] Slopes = { 10, 25, 35 };

        private static string F(float v, int digits = 3) => v.ToString("F" + digits, CultureInfo.InvariantCulture);
        private static string B(bool v) => v ? "true" : "false";

        private static string Physical(float friction, float bounce, float mass) =>
            "{\"mode\":\"physical\",\"mass\":" + mass.ToString(CultureInfo.InvariantCulture) + ",\"friction\":" + friction.ToString(CultureInfo.InvariantCulture) + ",\"bounce\":" + bounce.ToString(CultureInfo.InvariantCulture) + "}";

        private static PhysicsBodyDefinition Body(Guid id, string type, Vector3 at, Vector3? size = null, float friction = .5f, float bounce = 0f, float mass = 1f, Vector3 euler = default)
        {
            var s = size ?? Vector3.one; return new PhysicsBodyDefinition(id, type, new Vec3(at.x, at.y, at.z), new Vec3(euler.x, euler.y, euler.z), new Vec3(s.x, s.y, s.z), Physical(friction, bounce, mass), true);
        }

        private static PhysicsBodyDefinition Wall(Guid id, Vector3 at, Vector3 size) => new PhysicsBodyDefinition(id, "cube", new Vec3(at.x, at.y, at.z), new Vec3(0, 0, 0), new Vec3(size.x, size.y, size.z), "{}", true);

        /// <summary>The height of the slope ground at x: it rises with x between -40 and 40 (and |z| up to 40), as in the Godot project.</summary>
        public static float SlopeHeight(float x, float degrees) => Mathf.Clamp(x + 40f, 0f, 80f) * Mathf.Tan(degrees * Mathf.Deg2Rad);

        public static TerrainMap SlopeTerrain(float degrees)
        {
            var map = new TerrainMap(); var data = new byte[map.VertexCount * map.VertexCount * 2];
            for (var z = 0; z < map.VertexCount; z++)
                for (var x = 0; x < map.VertexCount; x++)
                {
                    var wx = x * map.CellSize - map.HalfExtent; var wz = z * map.CellSize - map.HalfExtent;
                    var h = Mathf.Abs(wz) <= 40f && wx <= 40f ? Mathf.Min(48f, SlopeHeight(wx, degrees)) : 0f;
                    var value = (ushort)Mathf.Clamp(Mathf.RoundToInt((h - TerrainMap.MinHeight) / (TerrainMap.MaxHeight - TerrainMap.MinHeight) * ushort.MaxValue), 0, ushort.MaxValue);
                    var i = (z * map.VertexCount + x) * 2; data[i] = (byte)(value & 0xFF); data[i + 1] = (byte)(value >> 8);
                }
            map.ImportHeights16(data); return map;
        }

        /// <summary>A flat ground at height zero everywhere the scenarios go.</summary>
        private static TerrainMap Flat() => new TerrainMap();

        private sealed class Run : IDisposable
        {
            public readonly UnityIslandPhysics Physics = new UnityIslandPhysics(); public int Frame;
            public Run(TerrainMap terrain, params PhysicsBodyDefinition[] defs) { Physics.SetTerrain(terrain); Physics.Sync(defs); }
            public float Time => Frame * Dt;
            public void Step() { Frame++; Physics.Step(Dt); }
            public Rigidbody Of(Guid id) => Physics.BodyOf(id);
            public void Dispose() => Physics.Dispose();
        }

        public static IReadOnlyList<string> Report()
        {
            var lines = new List<string> { "ENGINE physics=PhysX" };

            // ---- falling and resting (the original ones) and the bounce of a ball on a ball ----
            void Settle(string name, Guid id, float top, params PhysicsBodyDefinition[] defs)
            {
                using var run = new Run(Flat(), defs); var body = run.Of(id); var peak = 0f; var vmax = 0f; var landedAt = -1f;
                do
                {
                    run.Step(); var y = body.position.y;
                    if (landedAt < 0) { vmax = Math.Max(vmax, -body.linearVelocity.y); if (y - .5f <= top + .02f) landedAt = run.Time; }
                    if (landedAt >= 0) peak = Math.Max(peak, y);
                } while (!body.IsSleeping() && run.Time <= 14f);
                lines.Add($"RESULT {name} land_t={F(landedAt, 2)} impact_speed={F(vmax, 2)} rest_y={F(body.position.y)} peak_after_landing={F(peak)} time_to_sleep={F(run.Time, 2)} slept={B(body.IsSleeping())}");
            }
            var a = Guid.NewGuid(); Settle("drop", a, 0, Body(a, "sphere", new Vector3(5, 4.5f, 5)));
            var b = Guid.NewGuid(); Settle("bounce", b, 0, Body(b, "sphere", new Vector3(5, 5.5f, 5), bounce: 1f));
            var c = Guid.NewGuid(); Settle("on_box", c, 1, Wall(Guid.NewGuid(), new Vector3(12, .5f, 12), Vector3.one), Body(c, "sphere", new Vector3(12, 5.5f, 12)));
            {
                var e = Guid.NewGuid(); using var run = new Run(Flat(), Body(e, "sphere", new Vector3(5, .5f, 5))); var body = run.Of(e);
                for (var i = 0; i < 120; i++) run.Step();
                var x0 = body.position.x; run.Physics.Apply(e, "impulse", new Vec3(3.6f, 0, 0)); var start = run.Time;
                do run.Step(); while (!body.IsSleeping() && run.Time <= 14f);
                lines.Add($"RESULT kick roll_distance={F(body.position.x - x0)} time_to_sleep={F(run.Time - start, 2)} slept={B(body.IsSleeping())}");
            }
            var d = Guid.NewGuid(); Settle("box_drop", d, 0, Body(d, "cube", new Vector3(5, 3.5f, 5)));

            // ---- several bodies, rotation, speed ----
            {
                var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray(); using var run = new Run(Flat(), ids.Select((id, i) => Body(id, "cube", new Vector3(5, .5f + i * 1.05f, 5))).ToArray());
                bool Asleep() => ids.All(id => run.Of(id).IsSleeping());
                do run.Step(); while (!Asleep() && run.Time <= 14f);
                var drift = ids.Max(id => new Vector2(run.Of(id).position.x - 5, run.Of(id).position.z - 5).magnitude);
                lines.Add($"RESULT stack top_y={F(run.Of(ids[4]).position.y)} max_drift={F(drift)} time_to_sleep={F(run.Time, 2)} slept={B(Asleep())}");
            }
            {
                var id = Guid.NewGuid(); using var run = new Run(Flat(), Body(id, "cube", new Vector3(5, 4f, 5), euler: new Vector3(35, 20, 15))); var body = run.Of(id);
                do run.Step(); while (!body.IsSleeping() && run.Time <= 14f);
                var q = body.rotation; var flat = Mathf.Max(Mathf.Abs((q * Vector3.right).y), Mathf.Abs((q * Vector3.up).y), Mathf.Abs((q * Vector3.forward).y));
                lines.Add($"RESULT tilted rest_y={F(body.position.y)} flatness={F(flat, 4)} travelled={F(new Vector2(body.position.x - 5, body.position.z - 5).magnitude)} time_to_sleep={F(run.Time, 2)} slept={B(body.IsSleeping())}");
            }
            {
                var id = Guid.NewGuid(); using var run = new Run(Flat(), Body(id, "cube", new Vector3(5, .5f, 5))); var body = run.Of(id); run.Physics.Apply(id, "angular", new Vec3(0, 10, 0)); var angle = 0f;
                do { run.Step(); angle += body.angularVelocity.y * Dt; } while (!body.IsSleeping() && run.Time <= 14f);
                lines.Add($"RESULT spin turned_rad={F(angle, 2)} time_to_sleep={F(run.Time, 2)} slept={B(body.IsSleeping())}");
            }
            {
                var id = Guid.NewGuid(); using var run = new Run(Flat(), Wall(Guid.NewGuid(), new Vector3(10, 2, 0), new Vector3(.05f, 4, 4)), Body(id, "sphere", new Vector3(0, 2, 0), Vector3.one * .2f));
                run.Physics.Apply(id, "velocity", new Vec3(80, 0, 0)); while (run.Time < 1f) run.Step();
                lines.Add($"RESULT tunnel_wall passed={B(run.Of(id).position.x > 10.2f)} x={F(run.Of(id).position.x, 2)}");
            }
            {
                var id = Guid.NewGuid(); using var run = new Run(Flat(), Body(id, "sphere", new Vector3(5, 20, 5), Vector3.one * .2f)); run.Physics.Apply(id, "velocity", new Vec3(0, -80, 0)); var lowest = 100f;
                while (run.Time < 2f) { run.Step(); lowest = Math.Min(lowest, run.Of(id).position.y); }
                lines.Add($"RESULT tunnel_ground passed={B(lowest < -.5f)} min_y={F(lowest)}");
            }
            void Collide(string name, PhysicsBodyDefinition target, PhysicsBodyDefinition striker, float speed)
            {
                using var run = new Run(Flat(), target, striker); run.Physics.Apply(striker.Id, "velocity", new Vec3(speed, 0, 0)); float va = 0, vb = 0;
                while (run.Time < 6f) { run.Step(); if (run.Frame == 60) { va = run.Of(target.Id).linearVelocity.x; vb = run.Of(striker.Id).linearVelocity.x; } }
                lines.Add($"RESULT {name} target_speed_1s={F(va, 2)} striker_speed_1s={F(vb, 2)} target_x={F(run.Of(target.Id).position.x)} striker_x={F(run.Of(striker.Id).position.x)}");
            }
            Collide("collide", Body(Guid.NewGuid(), "sphere", new Vector3(0, .5f, 0)), Body(Guid.NewGuid(), "sphere", new Vector3(-3, .5f, 0)), 6f);
            Collide("collide_heavy", Body(Guid.NewGuid(), "cube", new Vector3(0, .5f, 0), mass: 10f), Body(Guid.NewGuid(), "sphere", new Vector3(-3, .5f, 0)), 10f);
            void Peak(string name, Guid id, float top, params PhysicsBodyDefinition[] defs)
            {
                using var run = new Run(Flat(), defs); var body = run.Of(id); var landed = false; var rising = false; var peak = 0f;
                while (run.Time < 4f)
                {
                    run.Step(); var y = body.position.y;
                    if (!landed) { if (y - .5f <= top + .02f) landed = true; }
                    else { if (body.linearVelocity.y > .05f) rising = true; if (rising) peak = Math.Max(peak, y); }
                }
                lines.Add($"RESULT {name} peak_after_landing={F(peak)}");
            }
            var bb = Guid.NewGuid(); Peak("ball_on_box_bounce", bb, 1, Body(Guid.NewGuid(), "cube", new Vector3(5, .5f, 5), bounce: .4f, mass: 5f), Body(bb, "sphere", new Vector3(5, 4.5f, 5), bounce: .4f));

            // ---- the materials: sliding and bouncing ----
            foreach (var (material, friction) in Frictions)
            {
                var id = Guid.NewGuid(); using var run = new Run(Flat(), Body(id, "cube", new Vector3(0, .5f, 0), friction: friction)); var body = run.Of(id); run.Physics.Apply(id, "velocity", new Vec3(6, 0, 0));
                do run.Step(); while (!body.IsSleeping() && !(run.Frame > 5 && body.linearVelocity.magnitude < .02f) && run.Time <= 14f);
                lines.Add($"RESULT slide_{material} distance={F(body.position.x)} stop_t={F(run.Time, 2)} upright={F((body.rotation * Vector3.up).y)}");
            }
            foreach (var (material, bounce) in Bounces) { var id = Guid.NewGuid(); Peak("material_bounce_" + material, id, 0, Body(id, "sphere", new Vector3(5, 3.5f, 5), bounce: bounce)); }

            // ---- on a slope ----
            foreach (var degrees in Slopes)
                foreach (var box in new[] { true, false })
                {
                    var id = Guid.NewGuid(); var x = 20f; var h = SlopeHeight(x, degrees);
                    var def = box ? Body(id, "cube", new Vector3(x, h + 1.6f, 0), new Vector3(4, 1, 4), mass: 20f, euler: new Vector3(0, 0, degrees)) : Body(id, "sphere", new Vector3(x, h + 1f, 0));
                    using var run = new Run(SlopeTerrain(degrees), def); var body = run.Of(id);
                    while (run.Time < (box ? 5f : 3f)) run.Step();
                    lines.Add($"RESULT slope_{(box ? "box" : "ball")}_{degrees} downhill={F(x - body.position.x)} speed={F(body.linearVelocity.magnitude, 2)} height_over_ground={F(body.position.y - SlopeHeight(body.position.x, degrees))}");
                }

            // ---- two hundred bodies ----
            {
                var ids = Enumerable.Range(0, 200).Select(_ => Guid.NewGuid()).ToArray();
                using var run = new Run(Flat(), ids.Select((id, i) => { var column = i % 10; var row = i / 10 % 10; var layer = i / 100; return Body(id, i % 2 == 1 ? "sphere" : "cube", new Vector3(column * 1.6f + .2f * layer, 1f + layer * 1.8f, row * 1.6f + .2f * layer)); }).ToArray());
                var watch = new Stopwatch(); double sum = 0, worst = 0; var steps = 0;
                while (run.Time < 8f) { watch.Restart(); run.Step(); var ms = watch.Elapsed.TotalMilliseconds; if (run.Frame > 5) { sum += ms; steps++; worst = Math.Max(worst, ms); } }
                var ys = ids.Select(id => run.Of(id).position.y).ToArray();
                lines.Add($"RESULT crowd asleep={ids.Count(id => run.Of(id).IsSleeping())} min_y={F(ys.Min())} max_y={F(ys.Max())} step_ms_avg={F((float)(sum / Math.Max(1, steps)))} step_ms_max={F((float)worst)}");
            }
            return lines;
        }
    }
}
