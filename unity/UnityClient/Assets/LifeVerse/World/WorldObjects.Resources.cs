using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// The "Recursos" of an object, as the Godot client draws them from the material JSON: a light ("light": colour, energy, range, falloff, shadow, and "face" + "cone" for a light that comes out of one face only),
    /// a projector ("projector": fov, focus, ambiance; with "projectorBuiltin" or "projectorTexture" for the picture it throws), and a reflection ("reflection": intensity, range) that mirrors the surroundings on shiny
    /// surfaces nearby. Lights and reflections cost the graphics card, so only the first ones in the world are on (<see cref="MaxLights"/>, <see cref="MaxLightShadows"/>, <see cref="MaxProbes"/>, from the settings);
    /// the rest stay dark until some go away.
    /// </summary>
    public sealed partial class WorldObjects
    {
        public static int MaxLights = 16, MaxLightShadows = 4, MaxProbes = 4;
        /// <summary>HDRP light units for an energy of 1 (a point light of energy 1.5 is a lamp; 8 a floodlight), the same scale as the lights of scripts.</summary>
        public const float LumensPerEnergy = 900f;
        private readonly List<Entry> _lamps = new List<Entry>(), _probes = new List<Entry>();
        public int LampsOn => _lamps.Count(e => e.Lamp != null && e.Lamp.activeSelf);
        public int LampsCasting => _lamps.Count(e => e.Lamp != null && e.Lamp.activeSelf && e.Lamp.GetComponentsInChildren<Light>().Any(l => l.shadows != LightShadows.None));
        public int ProbesOn => _probes.Count(e => e.Probe != null && e.Probe.activeSelf);

        /// <summary>The light of an object as its material says (null when it has none), for the window and the tests.</summary>
        public sealed class LightLook { public Color Color = Color.white; public float Energy = 1.5f, Range = 8f, Falloff = 1f, Cone = 60f; public bool Shadow; public int Face = -1; public float ProjectorFov = 45f, ProjectorFocus, Ambiance = .1f; public string ProjectorBuiltin; public Guid? ProjectorTexture; }

        public static LightLook ReadLight(string materialJson)
        {
            try
            {
                using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(materialJson) ? "{}" : materialJson); var root = d.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("light", out var light) || light.ValueKind != JsonValueKind.Object) return null;
                float N(JsonElement e, string key, float fallback) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && !double.IsNaN(n) && !double.IsInfinity(n) ? (float)n : fallback;
                var look = new LightLook
                {
                    Color = light.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String && ColorUtility.TryParseHtmlString(c.GetString(), out var parsed) ? parsed : Color.white,
                    Energy = Mathf.Clamp(N(light, "energy", 1f), 0f, 8f), Range = Mathf.Clamp(N(light, "range", 6f), .5f, 30f), Falloff = Mathf.Clamp(N(light, "falloff", 1f), .1f, 4f), Cone = Mathf.Clamp(N(light, "cone", 60f), 10f, 180f),
                    Shadow = light.TryGetProperty("shadow", out var s) && s.ValueKind == JsonValueKind.True, Face = light.TryGetProperty("face", out var f) && f.ValueKind == JsonValueKind.Number && f.TryGetInt32(out var face) ? face : -1,
                };
                var projector = root.TryGetProperty("projector", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
                look.ProjectorFov = Mathf.Clamp(N(projector, "fov", 45f), 10f, 120f); look.ProjectorFocus = Mathf.Clamp(N(projector, "focus", 0f), 0f, 30f); look.Ambiance = Mathf.Clamp(N(projector, "ambiance", .1f), 0f, 1f);
                if (root.TryGetProperty("projectorBuiltin", out var b) && b.ValueKind == JsonValueKind.String && BuiltInTextures.Has(b.GetString())) look.ProjectorBuiltin = b.GetString();
                if (root.TryGetProperty("projectorTexture", out var t) && t.ValueKind == JsonValueKind.String && Guid.TryParse(t.GetString(), out var asset)) look.ProjectorTexture = asset;
                return look;
            }
            catch (JsonException) { return null; }
        }

        /// <summary>The reflection of an object (intensity 0–1, range 2–30 m), or null.</summary>
        public static (float Intensity, float Range)? ReadReflection(string materialJson)
        {
            try
            {
                using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(materialJson) ? "{}" : materialJson); var root = d.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("reflection", out var r) || r.ValueKind != JsonValueKind.Object) return null;
                float N(string key, float fallback) => r.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && !double.IsNaN(n) ? (float)n : fallback;
                return (Mathf.Clamp(N("intensity", .6f), 0f, 1f), Mathf.Clamp(N("range", 8f), 2f, 30f));
            }
            catch (JsonException) { return null; }
        }

        private void ApplyResources(Entry entry, WorldObjectState state)
        {
            DropResources(entry);
            var look = ReadLight(state.MaterialJson);
            if (look != null)
            {
                var host = new GameObject("ObjectLight"); host.transform.SetParent(entry.Object.transform, false); entry.Lamp = host;
                var hasImage = look.ProjectorBuiltin != null || look.ProjectorTexture != null; var ambient = hasImage ? look.Ambiance : 1f;
                var face = look.Face >= 0 ? FaceSurface(entry, look.Face) : null;
                if (face is { } surface)
                {
                    // a light chosen for one face comes out of that face only: a cone in front of it
                    host.transform.position = surface.Centre + surface.Normal * .03f; host.transform.rotation = Quaternion.LookRotation(surface.Normal, Mathf.Abs(surface.Normal.y) > .95f ? Vector3.forward : Vector3.up);
                    Lamp(host, "Face", LightType.Spot, look.Color, look.Energy * ambient, look.Range, look.Cone, look.Shadow);
                }
                else Lamp(host, "Round", LightType.Point, look.Color, look.Energy * ambient, look.Range, 0f, look.Shadow);
                if (hasImage)
                {
                    var projector = Lamp(host, "Projector", LightType.Spot, look.Color, look.Energy, Mathf.Max(look.Range, look.ProjectorFocus), look.ProjectorFov, false);
                    if (face == null) projector.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                    var hd = projector.GetComponent<HDAdditionalLightData>();
                    if (look.ProjectorBuiltin != null) hd.SetCookie(BuiltInTextures.Get(look.ProjectorBuiltin));
                    else if (look.ProjectorTexture is { } asset && AssetLoader != null) Fetch(asset, true, texture => { if (hd != null && texture != null) hd.SetCookie(texture); });
                }
                _lamps.Add(entry);
            }
            if (ReadReflection(state.MaterialJson) is { } reflection)
            {
                var host = new GameObject("ObjectReflection"); host.transform.SetParent(entry.Object.transform, false); host.transform.localScale = new Vector3(1f / Mathf.Max(.001f, entry.Object.transform.lossyScale.x), 1f / Mathf.Max(.001f, entry.Object.transform.lossyScale.y), 1f / Mathf.Max(.001f, entry.Object.transform.lossyScale.z));
                entry.Probe = host;
                try
                {
                    host.AddComponent<ReflectionProbe>(); var probe = host.AddComponent<HDAdditionalReflectionData>();
                    probe.influenceVolume.shape = InfluenceShape.Box; probe.influenceVolume.boxSize = Vector3.one * reflection.Range; probe.mode = ProbeSettings.Mode.Realtime; probe.realtimeMode = ProbeSettings.RealtimeMode.OnDemand;
                    probe.multiplier = reflection.Intensity; probe.RequestRenderNextUpdate();
                }
                catch (Exception exception) { Debug.LogWarning("[LifeVerse] reflexo do objeto: " + exception.Message); }
                _probes.Add(entry);
            }
            RefreshBudget();
        }

        private static GameObject Lamp(GameObject host, string name, LightType type, Color color, float energy, float range, float angle, bool wantsShadow)
        {
            var go = new GameObject(name); go.transform.SetParent(host.transform, false);
            var light = go.AddComponent<Light>(); light.type = type; light.color = color; light.range = range; if (type == LightType.Spot) { light.spotAngle = Mathf.Clamp(angle, 1f, 179f); light.innerSpotAngle = light.spotAngle * .5f; }
            var hd = go.AddComponent<HDAdditionalLightData>(); hd.intensity = energy * LumensPerEnergy; light.shadows = LightShadows.None;
            go.AddComponent<WantsShadow>().On = wantsShadow; return go;
        }

        /// <summary>Marks the lights that asked for a shadow (the budget decides which get one).</summary>
        public sealed class WantsShadow : MonoBehaviour { public bool On; }

        private void DropResources(Entry entry)
        {
            if (entry.Lamp != null) Destroy(entry.Lamp); if (entry.Probe != null) Destroy(entry.Probe); entry.Lamp = entry.Probe = null;
            _lamps.Remove(entry); _probes.Remove(entry); RefreshBudget();
        }

        /// <summary>The first lights in the world (by when they appeared) are on, and the first of those that want it cast shadows; the same for reflections. Called when one comes or goes and when the settings change.</summary>
        public void RefreshBudget()
        {
            _lamps.RemoveAll(e => e.Lamp == null); _probes.RemoveAll(e => e.Probe == null); var shadows = 0;
            for (var i = 0; i < _lamps.Count; i++)
            {
                var on = i < Mathf.Clamp(MaxLights, 0, 64); _lamps[i].Lamp.SetActive(on);
                foreach (var light in _lamps[i].Lamp.GetComponentsInChildren<Light>(true))
                {
                    var wants = light.GetComponent<WantsShadow>(); var casts = on && wants != null && wants.On && shadows < Mathf.Clamp(MaxLightShadows, 0, 16); if (casts) shadows++;
                    light.shadows = casts ? LightShadows.Soft : LightShadows.None;
                }
            }
            for (var i = 0; i < _probes.Count; i++) _probes[i].Probe.SetActive(i < Mathf.Clamp(MaxProbes, 0, 16));
        }

        /// <summary>The middle and the outward direction of one face of an object in the world, from the triangles of its part of the mesh. Null when the object has no such face.</summary>
        private (Vector3 Centre, Vector3 Normal)? FaceSurface(Entry entry, int faceId)
        {
            var mesh = entry.Object.GetComponent<MeshFilter>()?.sharedMesh; if (mesh == null || mesh.subMeshCount < 2 || entry.State == null) return null;
            var sub = Array.IndexOf(FaceIdsOf(entry.State), faceId); if (sub < 0 || sub >= mesh.subMeshCount) return null;
            var triangles = mesh.GetTriangles(sub); if (triangles.Length < 3) return null; var vertices = mesh.vertices; var normals = mesh.normals;
            Vector3 centre = Vector3.zero, normal = Vector3.zero; foreach (var index in triangles) { centre += vertices[index]; if (normals.Length > index) normal += normals[index]; }
            centre /= triangles.Length; if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
            var t = entry.Object.transform; return (t.TransformPoint(centre), t.TransformDirection(normal).normalized);
        }

        /// <summary>The light host of an object (for the tests).</summary>
        public GameObject LampOf(Guid id) => _objects.TryGetValue(id, out var entry) ? entry.Lamp : null;
        public GameObject ProbeOf(Guid id) => _objects.TryGetValue(id, out var entry) ? entry.Probe : null;
    }
}
