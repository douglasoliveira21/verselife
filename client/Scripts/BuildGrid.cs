using Godot;

namespace LifeVerse.Client.Scripts;

/// A translucent 3D grid drawn under the selection while building, with the same spacing the move handles snap to.
/// It follows the selection one cell at a time and is only rebuilt when the cell or the spacing changes.
public partial class BuildGrid : MeshInstance3D
{
    private const int Lines = 12;
    private Vector3 _cell = new(float.NaN, 0, 0);
    private float _step;

    public override void _Ready()
    {
        Name = "BuildGrid"; Visible = false; CastShadow = ShadowCastingSetting.Off;
        MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, NoDepthTest = false };
    }

    /// Shows the grid around a point at a height; a spacing of 0 or less hides it.
    public void ShowAround(Vector3 center, float height, float step, bool visible)
    {
        Visible = visible && step > 0f;
        if (!Visible) return;
        var origin = new Vector3(Mathf.Snapped(center.X, step), height + .01f, Mathf.Snapped(center.Z, step));
        if (origin.IsEqualApprox(_cell) && Mathf.IsEqualApprox(step, _step)) return;
        _cell = origin; _step = step;
        GlobalPosition = origin;
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        var reach = Lines * step;
        for (var i = -Lines; i <= Lines; i++)
        {
            var strong = i % 5 == 0;
            var color = new Color(1, 1, 1, i == 0 ? .55f : strong ? .32f : .16f);
            mesh.SurfaceSetColor(color); mesh.SurfaceAddVertex(new Vector3(i * step, 0, -reach));
            mesh.SurfaceSetColor(color); mesh.SurfaceAddVertex(new Vector3(i * step, 0, reach));
            mesh.SurfaceSetColor(color); mesh.SurfaceAddVertex(new Vector3(-reach, 0, i * step));
            mesh.SurfaceSetColor(color); mesh.SurfaceAddVertex(new Vector3(reach, 0, i * step));
        }
        mesh.SurfaceEnd();
        Mesh = mesh;
    }
}
