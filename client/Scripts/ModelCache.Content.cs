using Godot;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// What else can be worn on the body besides a 3D model: a picture (a flat sign with that picture) and a saved construction (the shapes of a house, a lamp, a sofa). The file tells which it is, so
/// the same download serves all three and the person who sees an avatar needs to know nothing about the item.
/// </summary>
public partial class ModelCache
{
    /// <summary>How big a picture is when worn at scale 1: its longest side, in metres.</summary>
    public const float PictureMetres = .3f;

    private enum ContentKind { Model, Picture, Construction }

    private static ContentKind KindOf(byte[] b)
    {
        if (b.Length >= 4 && b[0] == (byte)'g' && b[1] == (byte)'l' && b[2] == (byte)'T' && b[3] == (byte)'F') return ContentKind.Model;
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == (byte)'P' && b[2] == (byte)'N' && b[3] == (byte)'G') return ContentKind.Picture;
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ContentKind.Picture;
        if (b.Length >= 12 && b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F' && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P') return ContentKind.Picture;
        var start = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;
        return b.Length > start && b[start] == (byte)'{' && System.Text.Encoding.UTF8.GetString(b, 0, Math.Min(b.Length, 256)).Contains(BuildPackages.FormatName, StringComparison.Ordinal) ? ContentKind.Construction : ContentKind.Model;
    }

    /// <summary>The picture as a flat sign, normalised like a model (longest side one metre; its real size is a few tens of centimetres).</summary>
    private static Entry PreparePicture(byte[] bytes)
    {
        var image = new Image();
        var error = bytes.Length >= 3 && bytes[0] == 0xFF ? image.LoadJpgFromBuffer(bytes) : bytes.Length >= 4 && bytes[0] == 0x89 ? image.LoadPngFromBuffer(bytes) : image.LoadWebpFromBuffer(bytes);
        if (error != Error.Ok || image.GetWidth() < 1 || image.GetHeight() < 1) return new Entry(null, Vector3.One);
        var longest = Mathf.Max(image.GetWidth(), image.GetHeight()); var size = new Vector2(image.GetWidth(), image.GetHeight()) / longest;
        var material = new StandardMaterial3D { AlbedoTexture = ImageTexture.CreateFromImage(image), Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, AlphaScissorThreshold = .1f, CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = 1 };
        var wrapper = new Node3D { Name = "Model" }; wrapper.AddChild(new MeshInstance3D { Name = "Picture", Mesh = new QuadMesh { Size = size }, MaterialOverride = material });
        return new Entry(wrapper, new Vector3(size.X, size.Y, .001f), new Vector3(size.X, size.Y, .002f) * PictureMetres);
    }

    /// <summary>The shapes of a saved construction, built the way the world builds them, centred and normalised to one metre. Every worn copy builds its own shapes (they need to be in the scene to fetch their pictures).</summary>
    private static Entry PrepareConstruction(byte[] bytes)
    {
        var package = BuildPackages.Parse(System.Text.Encoding.UTF8.GetString(bytes), out _);
        if (package is null || package.Objects.Length == 0) return new Entry(null, Vector3.One);
        var trial = new ConstructionBody(package); trial.Build();
        var bounds = Bounds(trial);
        if (bounds is not { } box || box.Size.LengthSquared() < 1e-8f) { trial.Free(); return new Entry(null, Vector3.One); }
        trial.Free();
        var longest = Mathf.Max(box.Size.X, Mathf.Max(box.Size.Y, box.Size.Z)); var factor = 1f / longest; var centre = box.Position + box.Size / 2f;
        return new Entry(null, box.Size * factor, box.Size, () =>
        {
            var body = new ConstructionBody(package); body.Build();
            body.Position = -centre * factor; body.Scale = Vector3.One * factor;
            var wrapper = new Node3D { Name = "Model" }; wrapper.AddChild(body); return wrapper;
        });
    }

    /// <summary>The shapes of a construction in one node. Built once outside the scene (so the size is known), and once more when it enters the scene, where its pictures can be fetched.</summary>
    public sealed partial class ConstructionBody(BuildPackage package) : Node3D
    {
        private readonly List<BuildObjectView> _views = [];
        public int Shapes => _views.Count;

        public void Build()
        {
            foreach (var o in package.Objects)
            {
                var view = new BuildObjectView();
                AddChild(view);
                view.Apply(new BuildObjectState
                {
                    Id = Guid.NewGuid(), PrimitiveType = o.PrimitiveType, Name = o.Name, GeometryJson = o.GeometryJson, MaterialJson = o.MaterialJson, PhysicsJson = "{\"collision\":false}", ContentJson = "[]",
                    PositionX = o.X, PositionY = package.BaseOffset + o.Y, PositionZ = o.Z, RotationX = o.RotationX, RotationY = o.RotationY, RotationZ = o.RotationZ, ScaleX = o.ScaleX, ScaleY = o.ScaleY, ScaleZ = o.ScaleZ,
                    PivotX = o.PivotX, PivotY = o.PivotY, PivotZ = o.PivotZ
                });
                _views.Add(view);
            }
        }

        public override void _EnterTree()
        {
            if (GetNodeOrNull("TextureCache") is not null) return;
            AddChild(new TextureCache { Name = "TextureCache" });
            foreach (var view in _views) view.Rebuild(); // now the pictures can come
        }
    }
}
