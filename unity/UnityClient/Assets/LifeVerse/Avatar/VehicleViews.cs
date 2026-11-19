using System.Collections.Generic;
using LifeVerse.Shared.Vehicles;
using LifeVerse.Unity.World;
using LifeVerse.Wire;
using UnityEngine;

namespace LifeVerse.Unity.Avatar
{
    /// <summary>One vehicle of the island: a simple car body with four wheels that follow the server's snapshots (position, heading, pitch, speed) and the driver's number.</summary>
    public sealed class VehicleView : MonoBehaviour
    {
        public int Id; public string VehicleName = "", VehicleType = "car";
        public int DriverPeer { get; private set; } public float Speed { get; private set; }
        public Vector3 GamePosition { get; private set; } public float Yaw { get; private set; }
        private Vector3 _target; private float _targetYaw, _targetPitch, _pitch, _spin; private bool _placed; private Transform[] _wheels;

        public static VehicleView Create(int id, string name, string type)
        {
            var root = new GameObject("Vehicle " + name); var view = root.AddComponent<VehicleView>(); view.Id = id; view.VehicleName = name; view.VehicleType = type; view.Build(); return view;
        }

        private static Material Surface(Color color, float smoothness)
        {
            var prototype = Resources.Load<Material>("LifeVerse/ObjectMaterial"); var material = new Material(prototype != null ? prototype : new Material(Shader.Find("HDRP/Lit")));
            material.SetColor("_BaseColor", color); material.SetFloat("_SmoothnessRemapMin", smoothness); material.SetFloat("_SmoothnessRemapMax", smoothness); material.SetFloat("_MetallicRemapMax", .2f); return material;
        }

        private static Transform Part(Transform parent, PrimitiveType shape, string name, Vector3 centre, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(shape); part.name = name; var collider = part.GetComponent<Collider>(); if (collider != null) { if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider); }
            part.transform.SetParent(parent, false); part.transform.localPosition = centre; part.transform.localScale = scale; part.GetComponent<MeshRenderer>().sharedMaterial = material; return part.transform;
        }

        private void Build()
        {
            var hue = Mathf.Abs((VehicleName + VehicleType).GetHashCode() % 360) / 360f; var paint = Surface(Color.HSVToRGB(hue, .65f, .75f), .7f); var dark = Surface(new Color(.08f, .08f, .09f), .4f); var glass = Surface(new Color(.45f, .6f, .7f), .9f);
            // the car faces +z of its own frame; origin on the ground at the middle
            Part(transform, PrimitiveType.Cube, "Body", new Vector3(0, .55f, 0), new Vector3(1.8f, .55f, 4.2f), paint);
            Part(transform, PrimitiveType.Cube, "Cabin", new Vector3(0, 1.05f, -.2f), new Vector3(1.55f, .5f, 2.1f), glass);
            _wheels = new Transform[4]; var i = 0;
            foreach (var x in new[] { -.95f, .95f }) foreach (var z in new[] { -1.4f, 1.4f }) { var wheel = Part(transform, PrimitiveType.Cylinder, "Wheel", new Vector3(x, .38f, z), new Vector3(.76f, .12f, .76f), dark); wheel.localRotation = Quaternion.Euler(0, 0, 90f); _wheels[i++] = wheel; }
        }

        public void Apply(VehicleSnapshot snapshot)
        {
            _target = new Vector3(snapshot.X, snapshot.Y, snapshot.Z); _targetYaw = snapshot.Yaw; _targetPitch = snapshot.Pitch; Speed = snapshot.Speed; DriverPeer = snapshot.DriverPeer;
            if (!_placed) { _placed = true; GamePosition = _target; Yaw = _targetYaw; _pitch = _targetPitch; Place(); }
        }

        public void PlaceAt(Vector3 game, float yaw) { _target = game; GamePosition = game; _targetYaw = Yaw = yaw; _placed = true; Place(); }

        private void Place()
        {
            transform.position = GameSpace.ToUnity(GamePosition); transform.rotation = GameSpace.RotationToUnity(_pitch * Mathf.Rad2Deg, Yaw * Mathf.Rad2Deg, 0f);
        }

        private void Update() => Tick(Time.deltaTime);

        public void Tick(float delta)
        {
            if (!_placed) return;
            var goal = _target + new Vector3(-Mathf.Sin(_targetYaw), 0f, -Mathf.Cos(_targetYaw)) * Speed * Mathf.Min(.1f, delta * 4f);   // a little ahead: the snapshots come at a few per second
            GamePosition = (goal - GamePosition).sqrMagnitude > 100f ? goal : Vector3.Lerp(GamePosition, goal, 1f - Mathf.Exp(-12f * delta));
            Yaw = Mathf.LerpAngle(Yaw * Mathf.Rad2Deg, _targetYaw * Mathf.Rad2Deg, 1f - Mathf.Exp(-12f * delta)) * Mathf.Deg2Rad; _pitch = Mathf.Lerp(_pitch, _targetPitch, 1f - Mathf.Exp(-10f * delta));
            Place();
            _spin += Speed * delta / .38f * Mathf.Rad2Deg; foreach (var wheel in _wheels) wheel.localRotation = Quaternion.Euler(_spin, 0f, 90f) ;
        }
    }

    /// <summary>One <see cref="VehicleView"/> for every vehicle the island server tells about; keeps them in step with its snapshots.</summary>
    public sealed class VehicleViews : MonoBehaviour
    {
        public WorldClient Client;
        private readonly Dictionary<int, VehicleView> _views = new Dictionary<int, VehicleView>(); private readonly Dictionary<int, VehicleSnapshot> _latest = new Dictionary<int, VehicleSnapshot>();
        public int Count => _views.Count;
        public VehicleView ViewOf(int id) => _views.TryGetValue(id, out var view) ? view : null;
        public IReadOnlyCollection<int> Ids => _views.Keys;
        public bool TryGetSnapshot(int id, out VehicleSnapshot snapshot) => _latest.TryGetValue(id, out snapshot);

        /// <summary>The vehicle this person drives now (or null), from the last snapshots.</summary>
        public VehicleView DrivenBy(int peer) { foreach (var pair in _latest) if (pair.Value.DriverPeer == peer) return _views.TryGetValue(pair.Key, out var view) ? view : null; return null; }

        /// <summary>The nearest vehicle to a place of the game within a range, or null (what a person can get into).</summary>
        public VehicleView Nearest(Vector3 game, float range)
        {
            VehicleView best = null; var bestDistance = range * range;
            foreach (var view in _views.Values) { var d = (view.GamePosition - game).sqrMagnitude; if (d <= bestDistance) { bestDistance = d; best = view; } }
            return best;
        }

        public void Attach(WorldClient client) { Client = client; client.VehicleSpawned += OnSpawned; client.VehicleSnapshotsReceived += OnSnapshots; }

        private void OnSpawned(VehicleSpawnedMessage car)
        {
            if (_views.TryGetValue(car.VehicleId, out var existing)) { existing.PlaceAt(new Vector3(car.X, car.Y, car.Z), car.Yaw); return; }
            var view = VehicleView.Create(car.VehicleId, car.Name, car.Type); view.transform.SetParent(transform, false); view.PlaceAt(new Vector3(car.X, car.Y, car.Z), car.Yaw); _views[car.VehicleId] = view;
        }

        private void OnSnapshots(IReadOnlyList<VehicleSnapshot> snapshots)
        {
            foreach (var snapshot in snapshots) { _latest[snapshot.Id] = snapshot; if (_views.TryGetValue(snapshot.Id, out var view)) view.Apply(snapshot); }
        }

        private void OnDestroy() { if (Client != null) { Client.VehicleSpawned -= OnSpawned; Client.VehicleSnapshotsReceived -= OnSnapshots; } }
    }
}
