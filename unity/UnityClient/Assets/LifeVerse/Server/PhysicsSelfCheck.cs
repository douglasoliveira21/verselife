using System;
using System.Collections.Generic;
using System.Linq;
using LifeVerse.IslandServer.Core;
using LifeVerse.IslandServer.Scripts;
using LifeVerse.Scripting;
using LifeVerse.Shared.Terrain;
using UnityEngine;

namespace LifeVerse.Unity.Server
{
    /// <summary>
    /// Checks the island physics on the real PhysX. Runs in the player with -lifeverse-physics-selfcheck (prints "event=physics_self_check result=passed|failed" and quits)
    /// and in the edit-mode tests. Everything it asserts is an observable consequence: a ball falls to the ground and sleeps, lands on a fixed box, is kicked by a walker,
    /// takes an impulse from a script, bounces with the right energy, and the ground follows the sculpted terrain.
    /// </summary>
    public static class PhysicsSelfCheck
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Environment.GetCommandLineArgs().Contains("-lifeverse-physics-selfcheck")) return;
            string failure = null; int checks = 0;
            try { checks = Run(); } catch (Exception exception) { failure = exception.Message + " " + exception.StackTrace; }
            Console.WriteLine(failure == null ? $"event=physics_self_check result=passed checks={checks}" : $"event=physics_self_check result=failed error={failure}");
            Application.Quit(failure == null ? 0 : 1);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootReport()
        {
            if (!Environment.GetCommandLineArgs().Contains("-lifeverse-physics-report")) return;
            try { foreach (var line in Report()) Console.WriteLine(line); Console.WriteLine("UNITY_DONE"); Application.Quit(0); }
            catch (Exception exception) { Console.WriteLine("UNITY_FAILED " + exception.Message); Application.Quit(1); }
        }

        /// <summary>
        /// The same scenarios, parameters and measurements as a small Godot project that reproduces island-server/Scripts/ServerPhysics.cs (RigidBody3D, damping .05/.6,
        /// material), so the two engines can be compared line by line. Flat ground at y = 0, 60 Hz, a 1 m ball (radius .5) or box.
        /// </summary>
        public static IReadOnlyList<string> Report()
        {
            const float dt = 1f / 60f; var lines = new List<string>(); var terrain = new TerrainMap();
            string Settle(string name, PhysicsBodyDefinition[] defs, Guid id, float groundTop)
            {
                using var physics = new UnityIslandPhysics(); physics.SetTerrain(terrain); physics.Sync(defs);
                var peak = 0f; var vmax = 0f; var landedAt = -1f; var frame = 0; BodyPose pose;
                do
                {
                    frame++; physics.Step(dt); pose = physics.Poses().Single(); var t = frame * dt;
                    if (landedAt < 0) { vmax = Math.Max(vmax, -(physics.VelocityOf(id)?.Y ?? 0f)); if (pose.Y - .5f <= groundTop + .02f) landedAt = t; }
                    if (landedAt >= 0) peak = Math.Max(peak, pose.Y);
                } while (!pose.Asleep && frame * dt <= 14f);
                return $"RESULT {name} land_t={landedAt:F2} impact_speed={vmax:F2} rest_y={pose.Y:F3} peak_after_landing={peak:F3} time_to_sleep={frame * dt:F2} slept={pose.Asleep.ToString().ToLowerInvariant()}";
            }
            var a = Guid.NewGuid(); lines.Add(Settle("drop", new[] { Def(a, "sphere", 5, 4.5f, 5, 1, Json("physical")) }, a, 0));
            var b = Guid.NewGuid(); lines.Add(Settle("bounce", new[] { Def(b, "sphere", 5, 5.5f, 5, 1, Json("physical", 1f, 1f)) }, b, 0));
            var box = Guid.NewGuid(); var c = Guid.NewGuid();
            lines.Add(Settle("on_box", new[] { Def(box, "cube", 12, .5f, 12, 1, "{}"), Def(c, "sphere", 12, 5.5f, 12, 1, Json("physical")) }, c, 1));
            var d = Guid.NewGuid(); lines.Add(Settle("box_drop", new[] { Def(d, "cube", 5, 3.5f, 5, 1, Json("physical")) }, d, 0));
            // kick: one second to settle, then one impulse of 3.6 N.s along +X (speed 4 * mass 1 * 0.9, what a walker gives); how far it rolls and when it stops
            using (var physics = new UnityIslandPhysics())
            {
                var e = Guid.NewGuid(); physics.SetTerrain(terrain); physics.Sync(new[] { Def(e, "sphere", 5, .5f, 5, 1, Json("physical")) });
                for (var i = 0; i < 120; i++) physics.Step(dt);
                var x0 = physics.Poses().Single().X; physics.Apply(e, "impulse", new Vec3(3.6f, 0, 0)); var frame = 0; BodyPose pose;
                do { frame++; physics.Step(dt); pose = physics.Poses().Single(); } while (!pose.Asleep && frame * dt <= 14f - 2f);
                lines.Add($"RESULT kick roll_distance={pose.X - x0:F3} time_to_sleep={frame * dt:F2} slept={pose.Asleep.ToString().ToLowerInvariant()}");
            }
            return lines;
        }

        private static string Json(string mode, float mass = 1f, float bounce = 0f) => $"{{\"mode\":\"{mode}\",\"mass\":{mass.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"bounce\":{bounce.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";

        private static PhysicsBodyDefinition Def(Guid id, string type, float x, float y, float z, float scale, string physics, float sx = 0, float sy = 0, float sz = 0) =>
            new(id, type, new Vec3(x, y, z), new Vec3(0, 0, 0), new Vec3(sx == 0 ? scale : sx, sy == 0 ? scale : sy, sz == 0 ? scale : sz), physics, true);

        private static void Expect(bool condition, string what) { if (!condition) throw new InvalidOperationException(what); }

        private static void Run(UnityIslandPhysics physics, float seconds) { for (var i = 0; i < seconds * 60; i++) physics.Step(1f / 60f); }

        public static int Run()
        {
            var checks = 0; var terrain = new TerrainMap();
            using var physics = new UnityIslandPhysics();
            physics.SetTerrain(terrain); Expect(physics.HasGround, "o terreno virou chão"); checks++;
            var ground = terrain.SampleHeight(5, 5);

            // 1. uma bola (raio 0,5) cai, quica pouco, para e dorme sobre o chão
            var ball = Guid.NewGuid(); var contacts = new List<(Guid, Guid?, bool, float)>();
            physics.Contacted = (self, other, entered, speed) => contacts.Add((self, other, entered, speed));
            physics.Sync(new[] { Def(ball, "sphere", 5, ground + 4, 5, 1, Json("physical")) });
            Expect(physics.BodyCount == 1, "um corpo"); checks++;
            Run(physics, 6);
            var pose = physics.Poses().Single();
            Expect(Math.Abs(pose.Y - (ground + .5f)) < .06f, $"a bola deveria repousar a {ground + .5f}, está em {pose.Y}"); checks++;
            Expect(pose.Asleep, "a bola deveria dormir"); checks++;
            Expect(contacts.Any(c => c.Item1 == ball && c.Item2 == null && c.Item3 && c.Item4 > 3f), "contato com o chão com a velocidade da queda"); checks++;

            // 2. uma caixa fixa (1 m de lado) recebe uma bola caindo em cima
            var box = Guid.NewGuid(); var second = Guid.NewGuid(); var boxGround = terrain.SampleHeight(12, 12);
            physics.Sync(new[] { Def(box, "cube", 12, boxGround + .5f, 12, 1, "{}"), Def(second, "sphere", 12, boxGround + 5, 12, 1, Json("physical")) });
            Expect(physics.BodyCount == 1 && physics.FixedCount == 1, "um corpo e uma caixa fixa (a primeira bola saiu da lista)"); checks++;
            Run(physics, 6);
            var onTop = physics.Poses().Single();
            Expect(Math.Abs(onTop.Y - (boxGround + 1f + .5f)) < .08f, $"a bola deveria estar sobre a caixa (y {boxGround + 1.5f}), está em {onTop.Y}"); checks++;

            // 3. quem anda empurra: a bola é chutada para longe dos pés, e dorme de novo longe dali
            var kicked = Guid.NewGuid(); var spot = terrain.SampleHeight(20, 20);
            physics.Sync(new[] { Def(kicked, "sphere", 20, spot + .5f, 20, 1, Json("physical", 1f)) });
            Run(physics, 3);
            var before = physics.Poses().Single();
            for (var i = 0; i < 40; i++) { physics.Push(19.2f, spot, 20, 4f, 0f); physics.Step(1f / 60f); }
            Run(physics, 1);
            var after = physics.Poses().Single();
            Expect(after.X > before.X + .5f, $"a bola deveria andar para +X ({before.X} -> {after.X})"); checks++;

            // 4. um script dá um impulso: a velocidade muda; "gravity" muda o peso aparente; ação desconhecida é recusada
            physics.Apply(kicked, "velocity", new Vec3(0, 0, 0)); Run(physics, 4);
            Expect(physics.Apply(kicked, "impulse", new Vec3(0, 6, 0)), "impulso aceito"); checks++;
            var velocity = physics.VelocityOf(kicked); Expect(velocity is { } v && v.Y > 4f, $"o impulso deveria subir a bola ({velocity})"); checks++;
            Expect(!physics.Apply(kicked, "explodir", new Vec3(1, 1, 1)), "ação desconhecida recusada"); checks++;
            Expect(!physics.Apply(Guid.NewGuid(), "impulse", new Vec3(1, 1, 1)), "objeto que não é corpo recusado"); checks++;

            // 5. quicar: uma bola com bounce 1 volta a uma boa parte da altura (a de bounce 0 não volta)
            var bouncy = Guid.NewGuid(); var floor = terrain.SampleHeight(30, 30);
            physics.Sync(new[] { Def(bouncy, "sphere", 30, floor + 5, 30, 1, Json("physical", 1f, 1f)) });
            var peak = 0f; var landed = false;
            for (var i = 0; i < 60 * 6; i++) { physics.Step(1f / 60f); var y = physics.Poses().Single().Y; if (!landed && y < floor + .6f) landed = true; if (landed) peak = Math.Max(peak, y - floor); }
            Expect(peak > 1.5f, $"a bola elástica deveria quicar para cima (pico {peak})"); checks++;

            // 6. o terreno esculpido vira o novo chão
            var settle = Guid.NewGuid();
            terrain.Apply(TerrainTool.Raise, 40, 40, TerrainMap.MaxBrushRadius, 1f); terrain.Apply(TerrainTool.Raise, 40, 40, TerrainMap.MaxBrushRadius, 1f);
            physics.SetTerrain(terrain);
            var raised = terrain.SampleHeight(40, 40);
            physics.Sync(new[] { Def(settle, "sphere", 40, raised + 4, 40, 1, Json("physical")) });
            Run(physics, 6);
            Expect(Math.Abs(physics.Poses().Single().Y - (raised + .5f)) < .12f, $"a bola deveria repousar no chão novo ({raised + .5f}), está em {physics.Poses().Single().Y}"); checks++;

            // 7. um objeto removido da lista some da simulação
            physics.Sync(Array.Empty<PhysicsBodyDefinition>());
            Expect(physics.BodyCount == 0 && physics.FixedCount == 0, "tudo removido"); checks++;
            return checks;
        }
    }
}
