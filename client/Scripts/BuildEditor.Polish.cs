using System.Text.Json;
using Godot;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Scripts;

/// Polish of the build window: the editing camera, the "saving…" indicator, the first-use tutorial, and loading a big island without a stall.
public partial class BuildEditor
{
    private const int LoadPerFrame = 40;
    private const string PrefsFile = "user://build_prefs.cfg";

    // ---- paced loading ---------------------------------------------------------------------------------------

    private readonly Queue<BuildObjectState> _loadQueue = new();
    public int LoadBacklogForTesting => _loadQueue.Count;

    /// An island with thousands of objects is loaded a few dozen per frame, so joining it does not freeze the game for a second.
    private void PumpLoad()
    {
        for (var i = 0; i < LoadPerFrame && _loadQueue.Count > 0; i++) AddOrUpdate(_loadQueue.Dequeue());
    }

    public void QueueStatesForTesting(IEnumerable<BuildObjectState> states) { foreach (var state in states) _loadQueue.Enqueue(state); }
    public void PumpLoadForTesting() => PumpLoad();

    // ---- editing camera --------------------------------------------------------------------------------------

    private BuildCamera _cameraRig = null!;
    private Button _cameraButton = null!;

    private void SetUpCamera()
    {
        _cameraRig = new BuildCamera { Name = "BuildCamera", Current = false }; AddChild(_cameraRig);
        _cameraRig.ActiveChanged += on =>
        {
            _network.LocalPlayer?.SetAvatarHidden(on); // the avatar must not stand in the way of what is being built
            if (_cameraButton is not null) _cameraButton.SetPressedNoSignal(on);
        };
    }

    public bool EditCameraActive => _cameraRig is not null && _cameraRig.Active;
    public BuildCamera EditCameraForTesting => _cameraRig;

    private void ToggleEditCamera()
    {
        if (_cameraRig.Active) { _cameraRig.Exit(); _status.Text = "Câmera normal."; return; }
        var previous = GetViewport().GetCamera3D();
        if (previous is null) return;
        var (center, radius) = SelectionSphere();
        _cameraRig.Enter(previous, center, selectionHasAny() ? OrbitMath.FocusDistance(radius, previous.Fov) : 10f);
        _status.Text = "Câmera de edição: botão direito gira, botão do meio (ou Shift + direito) desloca, roda dá zoom, Home foca na seleção.";
        bool selectionHasAny() => _selection.Count > 0;
    }

    /// Looks at the selection, switching to the editing camera first when needed.
    private void FocusSelection()
    {
        if (_selection.Count == 0) { _status.Text = "Selecione um objeto para focar."; return; }
        if (!_cameraRig.Active) ToggleEditCamera();
        var (center, radius) = SelectionSphere();
        _cameraRig.Focus(center, radius);
        _status.Text = "Focado na seleção.";
    }

    /// The middle and the reach of the selection, for pointing the camera at it.
    private (Vector3 Center, float Radius) SelectionSphere()
    {
        if (_selection.Count == 0) return (LookPoint(), 4f);
        var boxes = _selection.Select(WorldBounds).ToList();
        var min = boxes.Aggregate(new System.Numerics.Vector3(float.MaxValue), (a, b) => System.Numerics.Vector3.Min(a, b.Min));
        var max = boxes.Aggregate(new System.Numerics.Vector3(float.MinValue), (a, b) => System.Numerics.Vector3.Max(a, b.Max));
        var center = (min + max) / 2f;
        return (new Vector3(center.X, center.Y, center.Z), (max - min).Length() / 2f);
    }

    public void ToggleEditCameraForTesting() => ToggleEditCamera();
    public void FocusSelectionForTesting() => FocusSelection();

    // ---- saving indicator ------------------------------------------------------------------------------------

    private readonly Dictionary<Guid, int> _pending = [];
    private int _pendingCreates;
    private ulong _pendingSince;
    private Label _saveLabel = null!;

    /// Remembers an operation that waits for the server's answer, before it is sent (the answer may come back at once).
    private void TrackPending(string commandJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(commandJson); var root = doc.RootElement;
            var operation = root.GetProperty("operation").GetString();
            var wasIdle = _pending.Count == 0 && _pendingCreates == 0;
            if (operation == "create") _pendingCreates++;
            else if (operation is "update" or "delete" && root.TryGetProperty("objectId", out var id) && Guid.TryParse(id.GetString(), out var objectId)) _pending[objectId] = root.GetProperty("expectedRevision").GetInt32();
            else return;
            if (wasIdle) _pendingSince = Time.GetTicksMsec();
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
    }

    /// The server answered about this object (a newer revision, or it is gone).
    private void Settle(Guid objectId, int? revision)
    {
        if (_pending.TryGetValue(objectId, out var expected) && (revision is null || revision > expected)) _pending.Remove(objectId);
    }

    private void SettleCreate(BuildObjectState state) { if (IsMine(state) && _pendingCreates > 0) _pendingCreates--; }
    private void SettleAll() { _pending.Clear(); _pendingCreates = 0; }

    private void UpdateSaveLabel()
    {
        if (_saveLabel is null) return;
        var waiting = _pending.Count + _pendingCreates;
        if (waiting == 0) { _saveLabel.Text = "✓ Salvo"; _saveLabel.AddThemeColorOverride("font_color", UiStyle.SuccessBright); return; }
        var stalled = Time.GetTicksMsec() - _pendingSince > 8000;
        _saveLabel.Text = stalled ? "⚠ Sem resposta do servidor" : "Salvando…";
        _saveLabel.AddThemeColorOverride("font_color", stalled ? UiStyle.Orange : UiStyle.Accent);
    }

    public string SaveTextForTesting { get { UpdateSaveLabel(); return _saveLabel.Text; } }
    public int PendingForTesting => _pending.Count + _pendingCreates;
    public void ExpireSavingForTesting() { _pendingSince = Time.GetTicksMsec() - 9000; }

    // ---- tutorial --------------------------------------------------------------------------------------------

    private readonly BuildTutorial _tutorial = new();
    private Control _tutorialCard = null!;
    private Label _tutorialText = null!;

    private void BuildTutorialCard(VBoxContainer content)
    {
        var card = new PanelContainer(); card.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(UiStyle.AccentFill, .55f), UiStyle.Accent, 16, 2, 14, 8));
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); card.AddChild(row);
        var bulb = UiStyle.IconRect("bulb", UiStyle.Accent, 26); bulb.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; row.AddChild(bulb);
        _tutorialText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(300, 0) };
        _tutorialText.AddThemeFontSizeOverride("font_size", 17); row.AddChild(_tutorialText);
        var skip = new Button { Text = "Pular", TooltipText = "Fecha o tutorial; dá para repetir pelo botão Tutorial", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(76, 38) };
        UiStyle.StyleButton(skip, UiStyle.ButtonKind.Primary, 8, 4); skip.Pressed += SkipTutorial; row.AddChild(skip);
        _tutorialCard = card; card.Visible = false; content.AddChild(card);
    }

    private static bool TutorialDone()
    {
        try { var config = new ConfigFile(); return config.Load(PrefsFile) == Error.Ok && (bool)config.GetValue("tutorial", "done", false); }
        catch (Exception) { return false; }
    }

    private static void RememberTutorialDone()
    {
        var config = new ConfigFile(); config.Load(PrefsFile); config.SetValue("tutorial", "done", true); config.Save(PrefsFile);
    }

    private void StartTutorialIfNew() { if (!TutorialDone() && !_tutorial.Skipped && _tutorial.Current == TutorialStep.Create) RefreshTutorial(); }

    private void RestartTutorial() { _tutorial.Restart(); RefreshTutorial(); _status.Text = "Tutorial reiniciado."; }

    private void SkipTutorial() { _tutorial.Skip(); RememberTutorialDone(); RefreshTutorial(); }

    private void RefreshTutorial()
    {
        if (_tutorialCard is null) return;
        _tutorialCard.Visible = _tutorial.Active || (_tutorial.Finished && _tutorialShowFinish);
        _tutorialText.Text = BuildTutorial.Text(_tutorial.Current);
    }

    private bool _tutorialShowFinish;

    /// What the person just did, told to the tutorial; it moves on only when it was the step being asked for.
    private void Teach(TutorialStep did)
    {
        if (!_tutorial.Notify(did)) return;
        if (_tutorial.Finished) { RememberTutorialDone(); _tutorialShowFinish = true; GetTree().CreateTimer(8).Timeout += () => { _tutorialShowFinish = false; RefreshTutorial(); }; }
        RefreshTutorial();
    }

    /// Works out what kind of change an update was, to feed the tutorial.
    private void TeachFromChange(BuildObjectState before, BuildObjectState after)
    {
        if (before.PositionX != after.PositionX || before.PositionY != after.PositionY || before.PositionZ != after.PositionZ) Teach(TutorialStep.Move);
        if (before.ScaleX != after.ScaleX || before.ScaleY != after.ScaleY || before.ScaleZ != after.ScaleZ) Teach(TutorialStep.Resize);
        if (before.RotationX != after.RotationX || before.RotationY != after.RotationY || before.RotationZ != after.RotationZ) Teach(TutorialStep.Rotate);
        if (before.MaterialJson != after.MaterialJson) Teach(TutorialStep.Texture);
        if (before.GroupId != after.GroupId && after.GroupId is not null) Teach(TutorialStep.Link);
    }

    public TutorialStep TutorialStepForTesting => _tutorial.Current;
    public bool TutorialCardVisibleForTesting => _tutorialCard.Visible;
    public string TutorialTextForTesting => _tutorialText.Text;
    public void SkipTutorialForTesting() => SkipTutorial();
    /// Shows the tutorial card at a given step (1 to 8), for screenshots.
    public void PreviewTutorialForTesting(int step) { _tutorial.Restart(); for (var i = 1; i < step; i++) _tutorial.Notify((TutorialStep)(i - 1)); RefreshTutorial(); }
    public void RestartTutorialForTesting() => RestartTutorial();
    public void ForgetTutorialDoneForTesting() { var config = new ConfigFile(); config.Load(PrefsFile); config.SetValue("tutorial", "done", false); config.Save(PrefsFile); _tutorial.Restart(); }
}
