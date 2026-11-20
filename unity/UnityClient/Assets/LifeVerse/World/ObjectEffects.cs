using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// Particles and lights that scripts put on objects ("effect" from the island server): smoke, fire, sparks, rain, snow, explosions, trails, teleport bursts and dust, and point lights that can flicker. Counts are capped so a script
    /// cannot bury the screen, and the number of particles follows the graphics preset. An effect is identified by its key: the same key again changes it (and moves it with its object), "on": false takes it away.
    /// </summary>
    public sealed class ObjectEffects : MonoBehaviour
    {
        public const int MaxEmitters = 48, MaxLights = 16, MaxParticlesPerEmitter = 240;
        /// <summary>The share of particles drawn (the graphics preset sets it; 0 draws none).</summary>
        public float Quality = 1f;

        /// <summary>What each preset looks like: where it goes, how wide, how it falls (negative: rises), whether it adds light, whether it fires once, how much it grows.</summary>
        public readonly struct Look { public readonly Vector3 Direction; public readonly float Spread, Gravity, Grow; public readonly bool Additive, Once, Box; public Look(Vector3 direction, float spread, float gravity, bool additive, bool once, float grow, bool box = false) { Direction = direction; Spread = spread; Gravity = gravity; Additive = additive; Once = once; Grow = grow; Box = box; } }

        public static readonly Dictionary<string, Look> Looks = new Dictionary<string, Look>
        {
            ["smoke"] = new Look(Vector3.up, 18, -.6f, false, false, 2.2f), ["fire"] = new Look(Vector3.up, 14, -1.5f, true, false, .4f), ["sparks"] = new Look(Vector3.up, 55, 6f, true, false, .2f), ["rain"] = new Look(Vector3.down, 4, 9f, false, false, 1f, true),
            ["snow"] = new Look(Vector3.down, 25, .8f, false, false, 1f, true), ["explosion"] = new Look(Vector3.up, 180, 3f, true, true, 1.6f), ["trail"] = new Look(Vector3.zero, 6, 0f, true, false, .3f), ["teleport"] = new Look(Vector3.up, 25, -3f, true, true, .3f), ["dust"] = new Look(Vector3.up, 70, .3f, false, false, 1.5f),
        };

        private WorldClient _client; private readonly Dictionary<string, GameObject> _effects = new Dictionary<string, GameObject>(); private static Texture2D _soft; private Material _blend, _add;
        public int Emitters => _effects.Values.Count(e => e != null && e.GetComponent<ParticleSystem>() != null);
        public int Lights => _effects.Values.Count(e => e != null && e.GetComponent<Light>() != null);
        public GameObject EffectOf(string key) => _effects.TryGetValue(key, out var e) ? e : null;

        public void Attach(WorldClient client)
        {
            if (_client != null) _client.BuildOperation -= OnOperation; _client = client; if (client != null) client.BuildOperation += OnOperation;
        }

        private void OnDestroy() { if (_client != null) _client.BuildOperation -= OnOperation; }

        private void OnOperation(string json)
        {
            try { using var document = JsonDocument.Parse(json); if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("operation", out var op) && op.GetString() == "effect") OnEffect(document.RootElement); }
            catch (JsonException) { }
        }

        public void Clear() { foreach (var e in _effects.Values) if (e != null) Destroy(e); _effects.Clear(); }

        public void OnEffect(JsonElement root)
        {
            var key = root.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() ?? "" : ""; if (key.Length == 0 || key.Length > 80) return;
            if (!(root.TryGetProperty("on", out var on) && on.ValueKind == JsonValueKind.True)) { if (_effects.TryGetValue(key, out var gone)) { if (gone != null) Destroy(gone); _effects.Remove(key); } return; }
            double Number(string name, double fallback, double min, double max) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && !double.IsNaN(n) && !double.IsInfinity(n) ? Math.Max(min, Math.Min(max, n)) : fallback;
            string Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var position = GameSpace.ToUnity((float)Number("x", 0, -10000, 10000), (float)Number("y", 0, -10000, 10000), (float)Number("z", 0, -10000, 10000));
            var colour = ColorUtility.TryParseHtmlString(Text("color"), out var parsed) ? parsed : Color.white; var kind = Text("kind"); var flicker = root.TryGetProperty("flicker", out var f) && f.ValueKind == JsonValueKind.True;
            _effects.TryGetValue(key, out var existing); if (existing != null && (kind == "light") != (existing.GetComponent<Light>() != null)) { Destroy(existing); existing = null; _effects.Remove(key); }
            if (kind == "light")
            {
                if (existing == null) { if (Lights >= MaxLights) return; existing = new GameObject("Light " + key); existing.transform.SetParent(transform, false); existing.AddComponent<Light>().type = LightType.Point; existing.AddComponent<HDAdditionalLightData>(); _effects[key] = existing; }
                existing.transform.position = position; ConfigureLight(existing, colour, (float)Number("radius", 8, .5, 30), (float)Number("energy", 1.5, 0, 8), flicker);
            }
            else if (kind == "particles")
            {
                if (existing == null) { if (Quality <= 0f || Emitters >= MaxEmitters) return; existing = new GameObject("Particles " + key); existing.transform.SetParent(transform, false); var ps = existing.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); _effects[key] = existing; }
                existing.transform.position = position; ConfigureParticles(existing.GetComponent<ParticleSystem>(), Text("preset"), Math.Max(1, Number("rate", 20, 1, 60) * Quality), Number("lifetime", 2, .2, 6), Number("size", .4, .05, 2), Number("speed", 1.5, 0, 10), colour);
            }
        }

        private void ConfigureLight(GameObject go, Color colour, float radius, float energy, bool flicker)
        {
            var light = go.GetComponent<Light>(); light.color = colour; light.range = radius; light.shadows = LightShadows.None; var hd = go.GetComponent<HDAdditionalLightData>(); hd.intensity = energy * 900f;   // lumens: 1.5 is a lamp, 8 a floodlight
            hd.shadowUpdateMode = ShadowUpdateMode.OnDemand; var effect = go.GetComponent<EffectLight>() != null ? go.GetComponent<EffectLight>() : go.AddComponent<EffectLight>(); effect.Base = energy * 900f; effect.Flicker = flicker;
        }

        private Material MaterialFor(bool additive, Color colour)
        {
            ref var cached = ref (additive ? ref _add : ref _blend);
            if (cached == null) { var proto = Resources.Load<Material>(additive ? "LifeVerse/EffectMaterialAdd" : "LifeVerse/EffectMaterial"); cached = proto != null ? proto : new Material(Shader.Find("HDRP/Unlit")); }
            var material = new Material(cached); material.SetColor("_UnlitColor", new Color(colour.r, colour.g, colour.b, .8f)); material.SetTexture("_UnlitColorMap", Soft()); return material;
        }

        /// <summary>A round soft dot, so particles are puffs and not squares.</summary>
        public static Texture2D Soft()
        {
            if (_soft != null) return _soft; _soft = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp }; var pixels = new Color32[32 * 32];
            for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++) { var d = Mathf.Sqrt((x - 15.5f) * (x - 15.5f) + (y - 15.5f) * (y - 15.5f)) / 16f; var a = Mathf.Clamp01(1f - d); pixels[y * 32 + x] = new Color32(255, 255, 255, (byte)(a * a * 255f)); }
            _soft.SetPixels32(pixels); _soft.Apply(); return _soft;
        }

        private void ConfigureParticles(ParticleSystem ps, string preset, double rate, double lifetime, double size, double speed, Color colour)
        {
            if (!Looks.TryGetValue(preset ?? "", out var look)) look = Looks["smoke"];
            var main = ps.main; main.loop = !look.Once; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = (int)Math.Max(1, Math.Min(Math.Ceiling(rate * lifetime), MaxParticlesPerEmitter)); main.startLifetime = (float)lifetime;
            main.startSpeed = new ParticleSystem.MinMaxCurve((float)speed * .6f, (float)speed); main.startSize = new ParticleSystem.MinMaxCurve((float)size * .6f, (float)size); main.gravityModifier = look.Gravity / 9.81f;
            var emission = ps.emission; emission.enabled = true; if (look.Once) { emission.rateOverTime = 0f; emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)main.maxParticles) }); } else { emission.SetBursts(new ParticleSystem.Burst[0]); emission.rateOverTime = (float)rate; }
            var shape = ps.shape; shape.enabled = true; if (look.Box) { shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(12f, .2f, 12f); } else { shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = look.Spread * .5f; shape.radius = .05f; shape.scale = Vector3.one; }
            ps.transform.rotation = look.Direction == Vector3.zero ? Quaternion.identity : Quaternion.LookRotation(look.Direction, Vector3.forward);
            var grow = ps.sizeOverLifetime; grow.enabled = true; grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, .4f), new Keyframe(1f, look.Grow)));
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Billboard; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.sharedMaterial = MaterialFor(look.Additive, colour);
            if (!ps.isPlaying) ps.Play();
        }
    }

    /// <summary>Makes a point light flicker (a flame, a failing lamp) around its base brightness.</summary>
    public sealed class EffectLight : MonoBehaviour
    {
        public float Base = 1000f; public bool Flicker; private HDAdditionalLightData _hd; private float _seed;
        private void Awake() { _hd = GetComponent<HDAdditionalLightData>(); _seed = UnityEngine.Random.value * 7f; }
        private void Update() { if (_hd == null) return; var t = Time.time; _hd.intensity = Flicker ? Base * (.78f + .22f * Mathf.Sin(t * 23f + _seed) * Mathf.Sin(t * 7.3f)) : Base; }
    }
}
