using UnityEngine;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// Open ocean around the island: one large water plane that follows the camera so it reaches the horizon (snapped to a grid, because the ripples are computed from
    /// world position, so the snapping is invisible). Two normal maps slide over it at different speeds and scales for the waves; the transparent HDRP material shows
    /// the sea floor through the shallows. Seen from below the same surface is a translucent ceiling (the material is double sided). Swimming physics are the server's
    /// (AuthoritativeMovement); this is visual only. The plane sits at the sea level of the terrain map.
    /// </summary>
    public sealed class OceanView : MonoBehaviour
    {
        public const float PlaneSize = 2600f, GridStep = PlaneSize / 260f;
        private const float SwellTile = 70f, RippleTile = 9f;

        public Camera Camera;
        public float SeaLevel = -.8f;
        private Transform _plane;
        private Material _material;
        private bool _animate = true;

        public bool Animate { get => _animate; set => _animate = value; }
        public bool IsUnderwater { get; private set; }

        private void Awake()
        {
            var prototype = Resources.Load<Material>("LifeVerse/OceanMaterial");
            _material = prototype != null ? new Material(prototype) : new Material(Shader.Find("HDRP/Lit"));
            var plane = new GameObject("OceanPlane"); plane.transform.SetParent(transform, false); _plane = plane.transform;
            plane.AddComponent<MeshFilter>().sharedMesh = Grid(64);
            var renderer = plane.AddComponent<MeshRenderer>(); renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        }

        private static Mesh Grid(int cells)
        {
            var vertices = new Vector3[(cells + 1) * (cells + 1)]; var uv = new Vector2[vertices.Length]; var normals = new Vector3[vertices.Length]; var indices = new int[cells * cells * 6]; var n = 0;
            for (var z = 0; z <= cells; z++)
                for (var x = 0; x <= cells; x++) { var i = z * (cells + 1) + x; vertices[i] = new Vector3(((float)x / cells - .5f) * PlaneSize, 0, ((float)z / cells - .5f) * PlaneSize); uv[i] = new Vector2((float)x / cells, (float)z / cells); normals[i] = Vector3.up; }
            for (var z = 0; z < cells; z++)
                for (var x = 0; x < cells; x++) { var a = z * (cells + 1) + x; indices[n++] = a; indices[n++] = a + cells + 1; indices[n++] = a + 1; indices[n++] = a + 1; indices[n++] = a + cells + 1; indices[n++] = a + cells + 2; }
            var mesh = new Mesh { name = "OceanGrid", indexFormat = UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.triangles = indices; mesh.bounds = new Bounds(Vector3.zero, new Vector3(PlaneSize, 40f, PlaneSize));
            return mesh;
        }

        private void LateUpdate()
        {
            var camera = Camera != null ? Camera : UnityEngine.Camera.main; if (camera == null) return;
            var position = camera.transform.position;
            var planePosition = new Vector3(Mathf.Round(position.x / GridStep) * GridStep, SeaLevel, Mathf.Round(position.z / GridStep) * GridStep);
            _plane.position = planePosition; IsUnderwater = position.y < SeaLevel;
            var time = _animate ? Time.time : 0f;
            // texture coordinates come from world metres: scale = plane size / tile, offset = where the plane's corner is in tiles, plus the slide of the waves
            SetTexture("_NormalMap", SwellTile, planePosition, new Vector2(.020f, .012f) * time * SwellTile / 10f);
            SetTexture("_DetailMap", RippleTile, planePosition, new Vector2(-.05f, .035f) * time * RippleTile / 10f);
        }

        private void SetTexture(string name, float tile, Vector3 planePosition, Vector2 slide)
        {
            _material.SetTextureScale(name, new Vector2(PlaneSize / tile, PlaneSize / tile));
            _material.SetTextureOffset(name, new Vector2(planePosition.x / tile - PlaneSize / tile * .5f, planePosition.z / tile - PlaneSize / tile * .5f) + slide);
        }

        private void OnDestroy() { if (_material != null) Destroy(_material); }
    }
}
