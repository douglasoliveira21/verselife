using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts.Attachments;

/// <summary>"Meu Avatar → Anexos": everything worn on the body, grouped by region, with what can be done to each object.</summary>
public sealed partial class AttachmentsPage : VBoxContainer
{
    private static readonly (string Region, string Label)[] Regions = [(AttachmentPoints.Head, "Cabeça e rosto"), (AttachmentPoints.Torso, "Tronco"), (AttachmentPoints.Arms, "Braços e mãos"), (AttachmentPoints.Legs, "Pernas e pés"), (AttachmentPoints.Special, "Pontos especiais"), (AttachmentPoints.Hud, "HUD na tela")];

    private ItemList _list = null!;
    private Label _note = null!;
    private readonly List<AttachmentData?> _rows = [];
    private AttachmentStore? _store;

    /// <summary>The node the windows hang from.</summary>
    public Node? World { get; set; }
    /// <summary>Called before an action that opens the editor, so the window that holds this page can step aside.</summary>
    public Action? BeforeEdit { get; set; }
    public IReadOnlyList<AttachmentData?> Rows => _rows;
    public string Note => _note.Text;
    public int Count => _rows.Count(r => r is not null);

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8);
        AddChild(Caption("O que você está usando no corpo"));
        _list = new ItemList { CustomMinimumSize = new Vector2(0, 180), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill }; _list.AddThemeFontSizeOverride("font_size", 15); AddChild(_list);
        _list.ItemSelected += _ => Say(Selected?.Describe() ?? string.Empty);
        var first = new HBoxContainer(); first.AddThemeConstantOverride("separation", 6); AddChild(first);
        first.AddChild(Act("Editar", () => Do("edit"))); first.AddChild(Act("Alterar ponto", () => Do("move"))); first.AddChild(Act("Substituir", () => Do("move", replace: true)));
        var second = new HBoxContainer(); second.AddThemeConstantOverride("separation", 6); AddChild(second);
        second.AddChild(Act("Remover", () => Do("detach"))); second.AddChild(Act("Localizar no inventário", () => Do("locate"))); second.AddChild(Act("Propriedades", () => Do("properties")));
        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(380, 0) }; _note.AddThemeFontSizeOverride("font_size", 13); _note.AddThemeColorOverride("font_color", UiStyle.AccentText); AddChild(_note);
        _store = GetNodeOrNull<AvatarManager>("/root/AvatarManager")?.Attachments; if (_store is not null) { _store.Changed += Refresh; _store.Refresh(); }
        Refresh();
    }

    public override void _ExitTree() { if (_store is not null) _store.Changed -= Refresh; }

    private static Label Caption(string text) { var l = new Label { Text = text }; l.AddThemeFontSizeOverride("font_size", 15); l.AddThemeColorOverride("font_color", UiStyle.TextMuted); return l; }
    private static Button Act(string text, Action run) { var b = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true }; UiStyle.StyleButton(b, UiStyle.ButtonKind.Secondary, 10, 6); b.AddThemeFontSizeOverride("font_size", 13); b.Pressed += run; return b; }

    public AttachmentData? Selected => _list.GetSelectedItems() is { Length: > 0 } s && s[0] < _rows.Count ? _rows[s[0]] : null;

    /// <summary>Selects an object in the list as a click would.</summary>
    public void Select(Guid id) { var i = _rows.FindIndex(r => r?.Id == id); if (i >= 0) { _list.Select(i); Say(_rows[i]!.Describe()); } }

    public void Refresh()
    {
        if (_list is null) return;
        var keep = Selected?.Id; _list.Clear(); _rows.Clear();
        var worn = _store?.Worn.ToList() ?? [];
        foreach (var (region, label) in Regions)
        {
            var here = worn.Where(a => AttachmentPoints.Find(a.Point)?.Region == region).OrderBy(a => a.PointName).ThenBy(a => a.Order).ToList(); if (here.Count == 0) continue;
            _list.AddItem($"— {label} —", null, false); _list.SetItemDisabled(_list.ItemCount - 1, true); _rows.Add(null);
            foreach (var a in here) { _list.AddItem($"   {(string.IsNullOrWhiteSpace(a.Name) ? "Objeto" : a.Name)}  ·  {a.PointName}{(a.OriginalPointName is { } o ? $"  (era {o})" : string.Empty)}"); _rows.Add(a); }
        }
        if (_rows.Count == 0) { _list.AddItem("Nenhum objeto no corpo. Use o inventário: Anexar em…", null, false); _list.SetItemDisabled(0, true); _rows.Add(null); }
        if (keep is { } id) Select(id);
    }

    private void Say(string text) => _note.Text = text;

    private void Do(string action, bool replace = false)
    {
        if (Selected is not { } data) { Say("Escolha um objeto na lista."); return; }
        if (World is null) return;
        if (action == "edit") BeforeEdit?.Invoke();
        AttachmentActions.Run(World, action, data, Say, replace);
    }
}
