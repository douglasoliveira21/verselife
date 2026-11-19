#nullable enable
using System;
using System.Linq;
using System.Text;
using LifeVerse.Scripting;
using LifeVerse.Shared.Loading;
using LifeVerse.Shared.Networking;
using LifeVerse.Shared.Terrain;
using LifeVerse.Shared.Voice;
using UnityEngine;

namespace LifeVerse.Unity.Platform
{
    /// <summary>
    /// Verificação de fumaça dos plugins compartilhados (shared/scripting) dentro de um player compilado, não só no editor.
    /// Só roda com o argumento -lifeverse-selfcheck; imprime "event=plugin_self_check result=passed|failed" e encerra o player.
    /// </summary>
    public static class PluginSelfCheck
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            if (!Environment.GetCommandLineArgs().Contains("-lifeverse-selfcheck")) return;
            var failure = Verify();
            if (failure == null) Debug.Log("event=plugin_self_check result=passed checks=6");
            else Debug.LogError("event=plugin_self_check result=failed reason=" + failure);
            Application.Quit(failure == null ? 0 : 1);
        }

        private static string? Verify()
        {
            try
            {
                var snapshot = SnapshotCodec.Encode(42, new[] { new PlayerSnapshot(7, 12.345f, 1f, -2.5f, 3.2f, 0f, -1f, 1.2f, NetworkMotionState.Run, 99) });
                if (!SnapshotCodec.TryDecode(snapshot, out var decoded) || decoded.Players.Count != 1 || decoded.Players[0].Motion != NetworkMotionState.Run) return "snapshot";

                var map = new TerrainMap(); map.SetSeaLevel(1.5f);
                if (!TerrainMap.TryDeserialize(map.Serialize(), out var loaded) || Math.Abs(loaded.SeaLevel - 1.5f) > 1e-4f || Math.Abs(TerrainMap.FromJson(map.ToJson()).SeaLevel - 1.5f) > 1e-4f) return "terrain";

                if (!AssetPlanner.HashMatches("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", Encoding.ASCII.GetBytes("abc"))) return "sha256";

                var frame = new short[VoiceSettings.FrameSamples];
                for (var i = 0; i < frame.Length; i++) frame[i] = (short)(Math.Sin(i * 2 * Math.PI * 440 / VoiceSettings.SampleRate) * 8000);
                var encoder = new VoiceEncoder(); byte[] packet = Array.Empty<byte>();
                for (var warmUp = 0; warmUp < 5; warmUp++) packet = encoder.Encode(frame);
                var voice = new VoiceDecoder().Decode(packet);
                if (voice == null || VoiceLevel.Of(voice) <= 0.01f) return "opus";

                var endless = ScriptCompiler.Compile("function on_start() while true do end end");
                if (endless.Valid || !ScriptCompiler.Compile("function on_start() timer.every(1, function() end) end").Valid) return "lua";
                return WireSelfCheck.Run();
            }
            catch (Exception exception) { return exception.GetType().Name + ": " + exception.Message; }
        }
    }
}
