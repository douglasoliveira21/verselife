using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Models;

namespace LifeVerse.Client.Scripts;

public partial class CharacterCreator : Control
{
    private LineEdit _displayName = null!;
    private OptionButton _baseModel = null!;
    private OptionButton _hairStyle = null!;
    private OptionButton _top = null!;
    private OptionButton _bottom = null!;
    private OptionButton _shoes = null!;
    private OptionButton _accessory = null!;
    private ColorPickerButton _skin = null!;
    private ColorPickerButton _hair = null!;
    private ColorPickerButton _eyes = null!;
    private OptionButton _beard = null!;
    private HSlider _height = null!;
    private HSlider _shoulder = null!;
    private HSlider _sensitivity = null!;
    private CheckBox _invertY = null!;
    private Label _message = null!;
    private Button _save = null!;
    private AvatarRig _preview = null!;
    private Camera3D _camera = null!;
    private AvatarManager _manager = null!;
    private bool _dragging;
    private float _cameraDistance = 3.4f;
    private AvatarAppearanceData _lastSaved = AvatarManager.CreateDefault();
    private AvatarAppearanceData _undoValue = AvatarManager.CreateDefault();

    public override void _Ready()
    {
        _displayName = GetNode<LineEdit>("Layout/Panel/Margin/Fields/DisplayName");
        _baseModel = GetNode<OptionButton>("Layout/Panel/Margin/Fields/BaseModel");
        _hairStyle = GetNode<OptionButton>("Layout/Panel/Margin/Fields/HairStyle");
        _top = GetNode<OptionButton>("Layout/Panel/Margin/Fields/Top");
        _bottom = GetNode<OptionButton>("Layout/Panel/Margin/Fields/Bottom");
        _shoes = GetNode<OptionButton>("Layout/Panel/Margin/Fields/Shoes");
        _accessory = GetNode<OptionButton>("Layout/Panel/Margin/Fields/Accessory");
        _skin = GetNode<ColorPickerButton>("Layout/Panel/Margin/Fields/SkinColor");
        _hair = GetNode<ColorPickerButton>("Layout/Panel/Margin/Fields/HairColor");
        _sensitivity = GetNode<HSlider>("Layout/Panel/Margin/Fields/Sensitivity");
        _invertY = GetNode<CheckBox>("Layout/Panel/Margin/Fields/InvertY");
        _message = GetNode<Label>("Layout/Panel/Margin/Fields/Message");
        _save = GetNode<Button>("Layout/Panel/Margin/Fields/Buttons/Save");
        _preview = GetNode<AvatarRig>("Layout/Preview/SubViewport/AvatarRig");
        _camera = GetNode<Camera3D>("Layout/Preview/SubViewport/Camera3D");
        Fill(_baseModel, ("Classic", "human_a"), ("Athletic", "human_b"));
        Fill(_hairStyle, ("Short", "short"), ("Bob", "bob"), ("Mohawk", "mohawk"), ("Bald", "bald"));
        Fill(_top, ("Basic tee", "tee_basic"), ("Basic jacket", "jacket_basic"));
        Fill(_bottom, ("Basic pants", "pants_basic"), ("Basic shorts", "shorts_basic"));
        Fill(_shoes, ("Sneakers", "sneakers_basic"), ("Boots", "boots_basic"));
        Fill(_accessory, ("None", "none"), ("Round glasses", "glasses_round"), ("Basic hat", "hat_basic"));
        foreach (var option in new[] { _baseModel, _hairStyle, _top, _bottom, _shoes, _accessory }) option.ItemSelected += _ => RefreshPreview();
        _displayName.TextChanged += _ => RefreshPreview();
        _skin.ColorChanged += _ => RefreshPreview();
        _hair.ColorChanged += _ => RefreshPreview();
        _save.Pressed += Save;
        GetNode<Button>("Layout/Panel/Margin/Fields/Buttons/Cancel").Pressed += () => GetNode<UIManager>("/root/UIManager").ShowWorld();
        GetNode<SubViewportContainer>("Layout/Preview").GuiInput += PreviewInput;
        _manager = GetNode<AvatarManager>("/root/AvatarManager");
        AddAdvancedControls();
        _manager.AvatarLoaded += OnLoaded;
        _manager.AvatarSaved += OnSaved;
        InstallResponsiveScroll();
        SetBusy(true, "Loading avatar…");
        var error = _manager.Load();
        if (error != Error.Ok) OnLoaded(false, $"Could not load avatar: {error}");
    }

    public override void _ExitTree()
    {
        if (!IsInstanceValid(_manager)) return;
        _manager.AvatarLoaded -= OnLoaded;
        _manager.AvatarSaved -= OnSaved;
    }

    private void OnLoaded(bool success, string message)
    {
        SetBusy(false, success ? "" : message);
        var value = success ? _manager.Current : AvatarManager.CreateDefault();
        _lastSaved = value.Copy(); _undoValue = value.Copy(); ApplyToForm(value);
    }

    private void OnSaved(bool success, string message)
    {
        SetBusy(false, message);
        if (success) { _lastSaved = _manager.Current.Copy(); GetNode<UIManager>("/root/UIManager").ShowWorld(); }
    }

    private void Save()
    {
        var value = ReadForm();
        _undoValue = value.Copy();
        if (value.DisplayName.Trim().Length < 3) { _message.Text = "Display name must contain at least 3 characters."; return; }
        SetBusy(true, "Saving…");
        var error = _manager.Save(value);
        if (error != Error.Ok) OnSaved(false, $"Could not save avatar: {error}");
    }

    private void ApplyToForm(AvatarAppearanceData value)
    {
        _displayName.Text = value.DisplayName;
        Select(_baseModel, value.BaseModel); Select(_hairStyle, value.HairStyle);
        Select(_top, value.Item("top", "tee_basic")); Select(_bottom, value.Item("bottom", "pants_basic"));
        Select(_shoes, value.Item("shoes", "sneakers_basic")); Select(_accessory, value.Item("accessory", "none"));
        _skin.Color = Color.FromHtml(value.SkinColor); _hair.Color = Color.FromHtml(value.HairColor);
        if (IsInstanceValid(_eyes)) _eyes.Color = Color.FromHtml(value.EyeColor);
        if (IsInstanceValid(_beard)) Select(_beard, value.BeardStyle);
        var body = ParseBody(value.BodySettingsJson); if (IsInstanceValid(_height)) _height.Value = body.height; if (IsInstanceValid(_shoulder)) _shoulder.Value = body.shoulder;
        _sensitivity.Value = value.CameraSensitivity; _invertY.ButtonPressed = value.InvertCameraY;
        RefreshPreview();
    }

    private AvatarAppearanceData ReadForm() => new()
    {
        DisplayName = _displayName.Text.Trim(), BaseModel = Value(_baseModel), HairStyle = Value(_hairStyle),
        SkinColor = "#" + _skin.Color.ToHtml(false), HairColor = "#" + _hair.Color.ToHtml(false),
        EyeColor = IsInstanceValid(_eyes) ? "#" + _eyes.Color.ToHtml(false) : "#4A3326", BeardStyle = IsInstanceValid(_beard) ? Value(_beard) : "none",
        BodySettingsJson = $"{{\"height\":{(_height?.Value ?? 1):0.###},\"shoulder\":{(_shoulder?.Value ?? 1):0.###}}}",
        FaceSettingsJson = "{\"eyeScale\":1}", OutfitsJson = "[]", AttachmentsJson = "[]",
        CameraSensitivity = (float)_sensitivity.Value, InvertCameraY = _invertY.ButtonPressed,
        EquippedItems =
        [
            new EquippedItemData { Slot = "top", ItemKey = Value(_top), TintColor = "#3AAFC1" },
            new EquippedItemData { Slot = "bottom", ItemKey = Value(_bottom), TintColor = "#263B56" },
            new EquippedItemData { Slot = "shoes", ItemKey = Value(_shoes), TintColor = "#E7E9EE" },
            new EquippedItemData { Slot = "accessory", ItemKey = Value(_accessory), TintColor = "#E9C46A" }
        ]
    };

    private void RefreshPreview()
    {
        if (IsInstanceValid(_preview)) _preview.ApplyAppearance(ReadForm());
    }

    private void AddAdvancedControls()
    {
        var fields = GetNode<VBoxContainer>("Layout/Panel/Margin/Fields");
        _eyes = new ColorPickerButton { Name = "EyeColorAdvanced", CustomMinimumSize = new Vector2(0, 34) };
        _beard = new OptionButton { Name = "BeardStyleAdvanced" }; Fill(_beard, ("None", "none"), ("Stubble", "stubble"), ("Short beard", "short"), ("Full beard", "full"));
        _height = Slider("Height", .85, 1.15, 1); _shoulder = Slider("Shoulders", .9, 1.1, 1);
        fields.AddChild(new Label { Text = "Eye color" }); fields.AddChild(_eyes);
        fields.AddChild(new Label { Text = "Beard" }); fields.AddChild(_beard);
        fields.AddChild(new Label { Text = "Body proportions" }); fields.AddChild(_height); fields.AddChild(_shoulder);
        _eyes.ColorChanged += _ => RefreshPreview(); _beard.ItemSelected += _ => RefreshPreview(); _height.ValueChanged += _ => RefreshPreview(); _shoulder.ValueChanged += _ => RefreshPreview();
        var buttons = GetNode<HBoxContainer>("Layout/Panel/Margin/Fields/Buttons");
        var undo = new Button { Text = "Undo" }; var restore = new Button { Text = "Restore" }; buttons.AddChild(undo); buttons.AddChild(restore);
        undo.Pressed += () => ApplyToForm(_undoValue.Copy()); restore.Pressed += () => ApplyToForm(_lastSaved.Copy());
    }

    private static HSlider Slider(string name, double min, double max, double value) => new() { Name = name, MinValue = min, MaxValue = max, Step = .01, Value = value };
    private static (double height, double shoulder) ParseBody(string json)
    {
        try { using var doc = System.Text.Json.JsonDocument.Parse(json); var root = doc.RootElement; return (root.TryGetProperty("height", out var h) ? h.GetDouble() : 1, root.TryGetProperty("shoulder", out var s) ? s.GetDouble() : 1); }
        catch { return (1, 1); }
    }

    private void PreviewInput(InputEvent input)
    {
        if (input is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Left) _dragging = button.Pressed;
            if (button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                _cameraDistance = Mathf.Clamp(_cameraDistance + (button.ButtonIndex == MouseButton.WheelUp ? -.35f : .35f), 2.5f, 6f);
                _camera.Position = new Vector3(0, 1.25f, _cameraDistance);
            }
        }
        else if (input is InputEventMouseMotion motion && _dragging) _preview.RotateY(-motion.Relative.X * .01f);
    }

    private void SetBusy(bool busy, string message) { _save.Disabled = busy; _message.Text = message; }
    private void InstallResponsiveScroll()
    {
        var panel = GetNode<PanelContainer>("Layout/Panel");
        var margin = GetNode<MarginContainer>("Layout/Panel/Margin");
        panel.RemoveChild(margin);
        var scroll = new ScrollContainer
        {
            Name = "Scroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        panel.AddChild(scroll);
        scroll.AddChild(margin);
        margin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
    }
    private static void Fill(OptionButton option, params (string Label, string Value)[] items) { foreach (var item in items) { option.AddItem(item.Label); option.SetItemMetadata(option.ItemCount - 1, item.Value); } }
    private static string Value(OptionButton option) => option.GetItemMetadata(option.Selected).AsString();
    private static void Select(OptionButton option, string value) { for (var i = 0; i < option.ItemCount; i++) if (option.GetItemMetadata(i).AsString() == value) { option.Select(i); return; } }
}
