using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Scripts;

/// Seeing a whole construction before it is placed, and the ready-made ones ("Predefinidos"). A see-through copy of every object of the construction follows
/// the mouse over the ground; a click places it exactly there (the ordinary placing, with every server check), Esc cancels. Presets, constructions saved in the
/// inventory and the ones returned from the world all go through this.
public partial class BuildEditor
{
    private Node3D? _previewRoot;
    private Action<Vector3>? _previewConfirm;
    private int _previewParts;

    public bool PreviewActiveForTesting => _previewRoot is not null;
    public int PreviewPartsForTesting => _previewParts;
    public Vector3 PreviewAtForTesting => _previewRoot?.Position ?? Vector3.Zero;

    /// Shows the see-through copy of a construction and waits for the click. `confirm` receives the ground point the construction would stand on.
    public bool StartPreview(BuildPackage package, Action<Vector3> confirm)
    {
        if (!_active) SetActive(true);
        if (!_network.IsAuthenticated && OperationSink is null) { _status.Text = "Entre em uma ilha autenticada antes de construir."; return false; }
        DisarmPlacement(null);
        _previewRoot = new Node3D { Name = "ConstructionPreview", Visible = false }; GetParent().AddChild(_previewRoot);
        foreach (var o in package.Objects)
        {
            var shape = o.PrimitiveType == "mesh" ? "cube" : o.PrimitiveType; // a model is shown as its box until it is placed
            var view = new BuildObjectView(); _previewRoot.AddChild(view);
            view.Apply(new BuildObjectState { Id = Guid.NewGuid(), PrimitiveType = shape, Name = string.Empty, MaterialJson = GhostLook, PhysicsJson = "{\"collision\":false}", GeometryJson = o.PrimitiveType == "mesh" ? "{}" : o.GeometryJson,
                PositionX = o.X, PositionY = package.BaseOffset + o.Y, PositionZ = o.Z, RotationX = o.RotationX, RotationY = o.RotationY, RotationZ = o.RotationZ, ScaleX = o.ScaleX, ScaleY = o.ScaleY, ScaleZ = o.ScaleZ });
        }
        _previewParts = package.Objects.Length; _previewConfirm = confirm;
        var min = new Vector3(package.Objects.Min(o => o.X - o.ScaleX / 2), package.Objects.Min(o => o.Y - o.ScaleY / 2), package.Objects.Min(o => o.Z - o.ScaleZ / 2));
        var max = new Vector3(package.Objects.Max(o => o.X + o.ScaleX / 2), package.Objects.Max(o => o.Y + o.ScaleY / 2), package.Objects.Max(o => o.Z + o.ScaleZ / 2));
        var size = max - min;
        _status.Text = $"Prévia de \"{package.Name}\": {package.Objects.Length} objeto(s), {Disp(size.X, 1)} × {Disp(size.Y, 1)} × {Disp(size.Z, 1)} m. Clique para colocar; Esc cancela.";
        return true;
    }

    private void ClearPreview()
    {
        if (_previewRoot is not null && IsInstanceValid(_previewRoot)) _previewRoot.QueueFree();
        _previewRoot = null; _previewConfirm = null; _previewParts = 0;
    }

    /// Keeps the preview under the mouse (it hides over a window or when the mouse looks at nothing).
    private void UpdatePreview()
    {
        if (_previewRoot is null || !IsInstanceValid(_previewRoot)) return;
        var spot = GetViewport().GuiGetHoveredControl() is null ? SpotAt(GetViewport().GetMousePosition()) : null;
        _previewRoot.Visible = spot is not null;
        if (spot is { } where) _previewRoot.Position = where - new Vector3(0, .5f, 0);
    }

    private bool TryConfirmPreview(Vector2 screen)
    {
        if (_previewRoot is null) return false;
        if (SpotAt(screen) is not { } spot) { _status.Text = "Clique no terreno ou em um objeto para colocar."; return true; }
        ConfirmPreviewAt(spot - new Vector3(0, .5f, 0));
        return true;
    }

    private void ConfirmPreviewAt(Vector3 ground)
    {
        var confirm = _previewConfirm; ClearPreview();
        confirm?.Invoke(ground);
    }

    public void ConfirmPreviewForTesting(Vector3 ground) => ConfirmPreviewAt(ground);
    public void CancelPreviewForTesting() => DisarmPlacement("Prévia cancelada.");

    // ---- from the inventory -----------------------------------------------------------------------------------------

    /// Fetches a saved construction and shows its preview; the click places it. A construction that cannot be copied cannot be read by the client, so it is placed by the server where `fallback` is.
    public void PreviewPackage(Guid itemId, Vector3 fallback)
    {
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (!_network.IsAuthenticated && OperationSink is null) { _status.Text = "Entre em uma ilha autenticada antes de colocar construções."; return; }
        if (session is null || app is null || !session.IsAuthenticated) { _status.Text = "Entre na sua conta para usar o inventário."; return; }
        var http = new HttpRequest { Timeout = 30 }; AddChild(http);
        http.RequestCompleted += (_, code, _, body) =>
        {
            http.QueueFree();
            if (code == 403 && body.GetStringFromUtf8().Contains("use_rez", StringComparison.Ordinal)) { RezByServer(itemId, fallback); return; }
            if (code is < 200 or >= 300) { _status.Text = $"Não foi possível abrir a construção (HTTP {code})."; return; }
            var json = body.GetStringFromUtf8();
            if (BuildPackages.Parse(json, out var error) is not { } package) { _status.Text = $"Construção inválida: {error}"; return; }
            StartPreview(package, ground => RezPackageJson(json, ground));
        };
        if (http.Request($"{app.ApiBaseUrl}/api/v1/inventory/build-packages/{itemId}", [$"Authorization: Bearer {session.AccessToken}"]) != Error.Ok) { http.QueueFree(); _status.Text = "Não foi possível abrir a construção."; }
        else _status.Text = "Abrindo a construção…";
    }

    // ---- presets ------------------------------------------------------------------------------------------------------

    /// The "Predefinidos" card: ready-made constructions, each with a preview before it is placed.
    private PanelContainer BuildPresetsCard()
    {
        var card = Card("Predefinidos", "cube", out var body, out _, 10);
        body.AddChild(Txt("Construções prontas feitas de formas simples. Escolha uma, veja a prévia sobre o terreno e clique para colocar; depois dá para mexer em cada peça.", 18, false, UiStyle.TextMuted));
        var grid = new GridContainer { Columns = 3 }; grid.AddThemeConstantOverride("h_separation", 10); grid.AddThemeConstantOverride("v_separation", 10); body.AddChild(grid);
        foreach (var preset in BuildPresets.All)
        {
            var p = preset;
            var button = IconButton("cube", p.Name, $"{p.Description} ({p.Parts} peça(s))", () => PlacePreset(p.Id), UiStyle.ButtonKind.Secondary, 46); button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; grid.AddChild(button);
        }
        return card;
    }

    private void PlacePreset(string id)
    {
        if (BuildPresets.Find(id) is not { } preset) return;
        var json = BuildPackages.Serialize(preset.Package);
        StartPreview(preset.Package, ground => RezPackageJson(json, ground));
    }

    public void PlacePresetForTesting(string id) => PlacePreset(id);
}
