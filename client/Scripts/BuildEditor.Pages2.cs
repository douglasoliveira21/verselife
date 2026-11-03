using System.Text.Json.Nodes;
using Godot;

namespace LifeVerse.Client.Scripts;

/// The Textura, Recursos and Conteúdo tabs, laid out like the references in client/Assets/ui/construir. The controls keep writing to the
/// same material, light and content keys of the object as before.
public partial class BuildEditor
{
    private int _source;
    private readonly List<Button> _sourceButtons = [];
    private Label _previewName = null!, _previewInfo = null!, _appliedLabel = null!;
    private static ImageTexture? _placeholder;

    private static ImageTexture Placeholder
    {
        get
        {
            if (_placeholder is not null) return _placeholder;
            var image = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8); image.Fill(new Color("#2A3239"));
            return _placeholder = ImageTexture.CreateFromImage(image);
        }
    }

    // ---- the Textura tab ---------------------------------------------------------------------------------------

    private Control BuildTextureTab()
    {
        var page = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; page.AddThemeConstantOverride("separation", 12);

        // preview and target | colour and material | glow
        var top = Row(12); page.AddChild(top);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin }; left.AddThemeConstantOverride("separation", 10); top.AddChild(left);
        var preview = NewPreview(180); left.AddChild(preview);
        var target = Card("Alvo", "layers", out var targetBody, out _, 8);
        _target = Txt("Objeto inteiro", 20, true, UiStyle.AccentText); targetBody.AddChild(_target);
        _faceLabel = Txt("Face (tecla 5): clique em uma face.", 16, false, UiStyle.TextMuted); _faceLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; targetBody.AddChild(_faceLabel);
        var faceButtons = Row(8); targetBody.AddChild(faceButtons);
        faceButtons.AddChild(TextButton("Cor na face", "Pinta só a face selecionada com a cor ao lado", () => PaintFace(clear: false)));
        faceButtons.AddChild(TextButton("Limpar face", "Devolve a face ao material do objeto", () => PaintFace(clear: true)));
        left.AddChild(target);

        var middle = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; middle.AddThemeConstantOverride("separation", 10); top.AddChild(middle);
        var colour = Card("Cor da Textura", "texture", out var colourBody, out _, 12);
        var colourRow = Row(12); colourBody.AddChild(colourRow);
        _colorPicker = ColorButton(new Color("#A8C7D8"), 150); _colorPicker.TooltipText = "Cor do objeto (a textura é pintada por cima)"; colourRow.AddChild(_colorPicker);
        colourRow.AddChild(Swatches(QuickColors, color => { _colorPicker.Color = color; ScheduleMaterialApply(); }, 36));
        _colorPicker.PopupClosed += ScheduleMaterialApply;
        _roughness = NewSlider(0, 1, .01, .7); _metallic = NewSlider(0, 1, .01, 0); _opacity = NewSlider(0, 1, .01, 1);
        foreach (var slider in new[] { _roughness, _metallic, _opacity }) slider.DragEnded += _ => ScheduleMaterialApply();
        colourBody.AddChild(SliderLine("texture", "Rugosidade", _roughness, 2, true, 170));
        colourBody.AddChild(SliderLine("layers", "Metalicidade", _metallic, 2, true, 170));
        colourBody.AddChild(SliderLine("grid", "Opacidade", _opacity, 2, true, 170));
        middle.AddChild(colour);
        var propButtons = Row(10); middle.AddChild(propButtons);
        propButtons.AddChild(IconButton("copy", "Copiar propriedades", "Copia material e forma do objeto selecionado", CopyProperties, UiStyle.ButtonKind.Secondary, 46));
        propButtons.AddChild(IconButton("paste", "Colar propriedades", "Aplica material e forma copiados à seleção", PasteProperties, UiStyle.ButtonKind.Secondary, 46));

        var glowCard = Card("Brilho (Emissão)", "sun", out var glowBody, out var glowHeader, 12); glowCard.CustomMinimumSize = new Vector2(320, 0); glowCard.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        _glow = new CheckBox { Text = " Ligado", TooltipText = "O material passa a emitir luz própria" }; _glow.AddThemeFontSizeOverride("font_size", 19); _glow.Toggled += _ => ScheduleMaterialApply(); glowHeader.AddChild(_glow);
        _emissionPicker = ColorButton(Colors.White, 150); _emissionPicker.PopupClosed += ScheduleMaterialApply; glowBody.AddChild(_emissionPicker);
        glowBody.AddChild(Txt("Intensidade", 20));
        _glowStrength = NewSlider(0, 1, .01, .125); _glowStrength.DragEnded += _ => ScheduleMaterialApply();
        glowBody.AddChild(SliderLine(null, string.Empty, _glowStrength, 2, true, 0));
        top.AddChild(glowCard);

        // library
        var library = Card("Biblioteca de Texturas", "image", out var libraryBody, out var libraryHeader, 10);
        var importButton = OrangeButton("download", "Importar imagem", "Envia uma imagem (PNG, JPG ou WebP) ao seu inventário como textura", () => _imageDialog.PopupCentered(new Vector2I(800, 500)), 44); importButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; importButton.CustomMinimumSize = new Vector2(240, 44);
        _libraryStatus = Txt(string.Empty, 17, false, UiStyle.TextMuted); _libraryStatus.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; _libraryStatus.HorizontalAlignment = HorizontalAlignment.Right; _libraryStatus.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; _libraryStatus.CustomMinimumSize = new Vector2(380, 0);
        libraryHeader.AddChild(_libraryStatus); libraryHeader.AddChild(importButton);
        var libraryRow = Row(12); libraryRow.CustomMinimumSize = new Vector2(0, 176); libraryBody.AddChild(libraryRow);
        var sources = new VBoxContainer { CustomMinimumSize = new Vector2(230, 0) }; sources.AddThemeConstantOverride("separation", 8); libraryRow.AddChild(sources);
        var sourceNames = new[] { ("folder", "Minhas texturas"), ("star", "Favoritas"), ("history", "Recentes"), ("layers", "Texturas do sistema") };
        for (var i = 0; i < sourceNames.Length; i++)
        {
            var index = i;
            var button = ToolToggle(" " + sourceNames[i].Item2, "Mostra " + sourceNames[i].Item2.ToLowerInvariant(), () => SelectSource(index));
            button.CustomMinimumSize = new Vector2(0, 40); button.Icon = UiStyle.Icon(sourceNames[i].Item1, UiStyle.Text, 24); button.AddThemeConstantOverride("icon_max_width", 24); button.ExpandIcon = false; button.Alignment = HorizontalAlignment.Left; button.AddThemeFontSizeOverride("font_size", 19);
            _sourceButtons.Add(button); sources.AddChild(button);
        }
        _library = new ItemList { MaxColumns = 4, SameColumnWidth = true, IconMode = ItemList.IconModeEnum.Top, FixedIconSize = new Vector2I(104, 80), SelectMode = ItemList.SelectModeEnum.Single, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, FixedColumnWidth = 118 };
        var none = new StyleBoxEmpty(); var chosen = UiStyle.Box(new Color(0, 0, 0, 0), UiStyle.Accent, 10, 3, 4, 4);
        _library.AddThemeStyleboxOverride("panel", none); _library.AddThemeStyleboxOverride("focus", none); _library.AddThemeStyleboxOverride("selected", chosen); _library.AddThemeStyleboxOverride("selected_focus", chosen); _library.AddThemeStyleboxOverride("hovered", UiStyle.Box(new Color(1, 1, 1, .06f), new Color(0, 0, 0, 0), 10, 0, 4, 4));
        _library.ItemSelected += index => ShowPreview((int)index);
        libraryRow.AddChild(_library);

        var previewCard = Card("Prévia da Textura", null, out var previewBody, out _, 8); previewCard.CustomMinimumSize = new Vector2(290, 0); previewCard.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        _preview = new TextureRect { CustomMinimumSize = new Vector2(0, 150), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, SizeFlagsVertical = Control.SizeFlags.ExpandFill, ClipContents = true };
        previewBody.AddChild(_preview);
        _previewName = Txt("—", 19, true); previewBody.AddChild(_previewName);
        _previewInfo = Txt(string.Empty, 17, false, UiStyle.TextMuted); previewBody.AddChild(_previewInfo);
        libraryRow.AddChild(previewCard);
        page.AddChild(library);

        // UV
        var uv = Card("UV (Mapeamento)", "expand", out var uvBody, out var uvHeader, 10);
        var uvRow = Row(18); uvBody.AddChild(uvRow);
        var repeatFields = new LineEdit[2]; var offsetFields = new LineEdit[2];
        uvRow.AddChild(UvGroup("Repetição (Escala)", repeatFields, 1));
        uvRow.AddChild(new VSeparator());
        uvRow.AddChild(UvGroup("Deslocamento (Offset)", offsetFields, 0));
        _repeatX = repeatFields[0]; _repeatY = repeatFields[1]; _offsetX = offsetFields[0]; _offsetY = offsetFields[1];
        uvRow.AddChild(new VSeparator());
        var turn = new VBoxContainer { CustomMinimumSize = new Vector2(200, 0) }; turn.AddThemeConstantOverride("separation", 6); uvRow.AddChild(turn);
        var turnTitle = Row(8); turnTitle.AddChild(UiStyle.IconRect("rotate", UiStyle.Text, 24)); turnTitle.AddChild(Txt("Rotação", 20)); turn.AddChild(turnTitle);
        var turnRow = Row(0); turn.AddChild(turnRow);
        _uvRotation = Field("0", 8); _uvRotation.Text = Disp(0, 2); _uvRotation.CustomMinimumSize = new Vector2(100, 44); turnRow.AddChild(_uvRotation); turnRow.AddChild(Stepper(_uvRotation, () => 15f, () => { }));
        turnRow.AddChild(Txt("°", 20));
        uvRow.AddChild(new VSeparator());
        var flips = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; flips.AddThemeConstantOverride("separation", 6); uvRow.AddChild(flips);
        _flipU = new CheckBox { Text = " Espelhar no eixo X" }; _flipV = new CheckBox { Text = " Espelhar no eixo Y" };
        foreach (var flip in new[] { _flipU, _flipV }) { flip.AddThemeFontSizeOverride("font_size", 19); flips.AddChild(flip); }
        var applyUv = IconButton("check", "Aplicar UV", "Aplica repetição, deslocamento, rotação e espelhamento ao alvo", ApplyUv, UiStyle.ButtonKind.Secondary, 44); applyUv.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; applyUv.CustomMinimumSize = new Vector2(220, 44); uvHeader.AddChild(applyUv);
        page.AddChild(uv);

        // footer
        var footer = Card(null, null, out var footerBody, out _, 0, false, 16, 8); page.AddChild(footer);
        var footerRow = Row(12); footerBody.AddChild(footerRow);
        footerRow.AddChild(UiStyle.IconRect("info", UiStyle.Text, 26));
        footerRow.AddChild(Txt("Textura aplicada:", 20)); _appliedLabel = Txt("nenhuma", 20, true, UiStyle.AccentText); footerRow.AddChild(_appliedLabel);
        footerRow.AddChild(Filler());
        var remove = IconButton("trash", "Remover", "Tira a textura do alvo", RemoveTexture, UiStyle.ButtonKind.Secondary, 48); remove.CustomMinimumSize = new Vector2(180, 48); remove.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; footerRow.AddChild(remove);
        var normal = OrangeButton("layers", "Como mapa normal", "Usa a imagem escolhida como relevo", () => ApplyTexture(normal: true), 48); normal.CustomMinimumSize = new Vector2(260, 48); normal.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; footerRow.AddChild(normal);
        var apply = IconButton("check", "Aplicar textura", "Usa a textura escolhida no alvo (objeto ou face selecionada)", () => ApplyTexture(normal: false), UiStyle.ButtonKind.Primary, 48); apply.CustomMinimumSize = new Vector2(260, 48); apply.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; footerRow.AddChild(apply);

        _imageDialog = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = ["*.png,*.jpg,*.jpeg,*.webp ; Imagens"], Title = "Importar textura" };
        _imageDialog.FileSelected += ImportImage;
        AddChild(_imageDialog);
        _sourceButtons[0].SetPressedNoSignal(true);
        RefreshLibrary();
        return page;
    }

    private VBoxContainer UvGroup(string title, LineEdit[] fields, double start)
    {
        var group = new VBoxContainer { CustomMinimumSize = new Vector2(290, 0) }; group.AddThemeConstantOverride("separation", 6);
        group.AddChild(Txt(title, 20));
        var row = Row(10); group.AddChild(row);
        for (var i = 0; i < 2; i++) { row.AddChild(AxisField(fields, i, () => .1f, _ => { })); fields[i].Text = Disp(start, 2); }
        return group;
    }

    private int _applyToken;

    /// Material changes (colour, shine, opacity, glow) are sent a moment after the person lets go of the control, not on every tick.
    private void ScheduleMaterialApply()
    {
        if (_filling) return;
        var token = ++_applyToken;
        GetTree().CreateTimer(.35).Timeout += () => { if (token == _applyToken && _selection.Count > 0) ApplySelected(); };
    }

    private void SelectSource(int index)
    {
        _source = index;
        for (var i = 0; i < _sourceButtons.Count; i++) _sourceButtons[i].SetPressedNoSignal(i == index);
        RefreshLibrary();
    }

    // ---- the Recursos tab --------------------------------------------------------------------------------------

    private Control BuildResourcesTab()
    {
        var page = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; page.AddThemeConstantOverride("separation", 12);
        var main = Row(12); main.SizeFlagsVertical = Control.SizeFlags.ExpandFill; page.AddChild(main);
        var preview = NewPreview(480); preview.CustomMinimumSize = new Vector2(430, 480); preview.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; main.AddChild(preview);

        var card = Card("Emitir luz", "bulb", out var body, out var header, 14); card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _lightOn = new CheckBox { Text = " Ativado", TooltipText = "O objeto passa a iluminar o que está perto (até 16 luzes acesas por vez no seu jogo)" }; _lightOn.AddThemeFontSizeOverride("font_size", 20); header.AddChild(_lightOn);
        body.AddChild(Txt("Cor da luz", 21, true));
        var colourRow = Row(14); body.AddChild(colourRow);
        _lightColor = ColorButton(new Color("#FFE0A8"), 200); colourRow.AddChild(_lightColor);
        colourRow.AddChild(Swatches(LightColors, color => _lightColor.Color = color, 46));
        body.AddChild(Rule());
        _lightEnergy = NewSlider(0, 8, .1, 1.5); _lightRange = NewSlider(.5, 30, .5, 8);
        body.AddChild(SliderLine("sun", "Intensidade", _lightEnergy, 2, true, 170));
        body.AddChild(SliderLine("waves", "Alcance", _lightRange, 2, true, 170));
        body.AddChild(Rule());
        var apply = IconButton("check", "Aplicar luz", "Envia a luz ao servidor; desligue Ativado para remover", ApplyLight, UiStyle.ButtonKind.Primary, 62); apply.AddThemeFontSizeOverride("font_size", 24); body.AddChild(apply);
        body.AddChild(Filler());
        main.AddChild(card);
        page.AddChild(NoteBar("Nota:", "A luz deixa o ambiente mais real, mas luzes demais pesam no desempenho: só as 16 mais próximas ficam acesas por vez. O brilho do material fica na aba Textura."));
        return page;
    }

    // ---- the Conteúdo tab --------------------------------------------------------------------------------------

    private int _contentSelected = -1;
    private readonly List<PanelContainer> _contentRowNodes = [];
    private readonly List<string> _contentTexts = [];
    private VBoxContainer _contentRows = null!;
    private Label _contentCount = null!;

    private static string CategoryIcon(string category) => category switch
    {
        "scripts" => "code", "textures" => "image", "sounds" => "volume", "animations" => "run", "models" => "cube", "packages" => "layers", "clothing" => "avatar",
        "accessories" => "star", "objects" => "cube", "notecards" => "list", _ => "folder"
    };

    private Control BuildContentTab()
    {
        var page = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; page.AddThemeConstantOverride("separation", 12);
        var main = Row(12); main.SizeFlagsVertical = Control.SizeFlags.ExpandFill; page.AddChild(main);
        var preview = NewPreview(380); preview.CustomMinimumSize = new Vector2(400, 380); preview.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; main.AddChild(preview);

        var card = Card("Conteúdo do Objeto", "folder", out var body, out var header, 10, false); card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _contentCount = Txt("0 itens", 19); var badge = new PanelContainer(); badge.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(UiStyle.FieldFill, .8f), UiStyle.Border, 10, 1, 14, 5)); badge.AddChild(_contentCount); header.AddChild(badge);
        var help = UiStyle.IconRect("info", UiStyle.Text, 26); help.TooltipText = "Itens guardados dentro do objeto. Só referências: os arquivos continuam no seu inventário."; help.MouseFilter = Control.MouseFilterEnum.Pass; header.AddChild(help);

        var head = PanelWith(UiStyle.Box(new Color(UiStyle.FieldFill, .55f), new Color(UiStyle.Border, .5f), 10, 1, 14, 8));
        var headRow = Row(8); head.AddChild(headRow);
        var itemHead = Txt("Item", 20, true); itemHead.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; headRow.AddChild(itemHead);
        var typeHead = Txt("Tipo", 20, true); typeHead.CustomMinimumSize = new Vector2(250, 0); headRow.AddChild(typeHead);
        var actionsHead = Txt("Ações", 20, true); actionsHead.CustomMinimumSize = new Vector2(90, 0); actionsHead.HorizontalAlignment = HorizontalAlignment.Center; headRow.AddChild(actionsHead);
        body.AddChild(head);
        _contentRows = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; _contentRows.AddThemeConstantOverride("separation", 4); body.AddChild(_contentRows);
        _contentInfo = Txt("Selecione um objeto.", 17, false, UiStyle.TextMuted); body.AddChild(_contentInfo);
        main.AddChild(card);

        var lower = Row(14); page.AddChild(lower);
        var add = OrangeButton("plus", "Adicionar do inventário…", "Escolhe um item do seu inventário para guardar no objeto", OpenPicker, 56); add.CustomMinimumSize = new Vector2(420, 56); add.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; lower.AddChild(add);
        lower.AddChild(new VSeparator());
        var rename = Card(null, null, out var renameBody, out _, 6, false, 14, 8); lower.AddChild(rename);
        renameBody.AddChild(Txt("Renomear item:", 19));
        var renameRow = Row(10); renameBody.AddChild(renameRow);
        _contentName = Field("Novo nome", 120); _contentName.CustomMinimumSize = new Vector2(0, 46); _contentName.TextSubmitted += _ => RenameContent(); renameRow.AddChild(_contentName);
        var renameButton = OrangeButton("copy", "Renomear", "Muda o nome do item selecionado dentro do objeto", RenameContent, 46); renameButton.CustomMinimumSize = new Vector2(190, 46); renameButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; renameRow.AddChild(renameButton);
        var remove = DangerButton("trash", "Remover", "Tira o item do objeto (ele continua no seu inventário)", RemoveContent); remove.CustomMinimumSize = new Vector2(190, 46); remove.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; renameRow.AddChild(remove);
        page.AddChild(NoteBar("Dica:", "Adicione sons, texturas, modelos 3D e outros recursos do seu inventário para compor este objeto."));

        _pickerDialog = new AcceptDialog { Title = "Adicionar ao objeto", OkButtonText = "Adicionar", Exclusive = true, MinSize = new Vector2I(420, 420) };
        _picker = new ItemList { CustomMinimumSize = new Vector2(380, 320), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _pickerDialog.AddChild(_picker);
        _pickerDialog.Confirmed += () => { var chosen = _picker.GetSelectedItems(); if (chosen.Length == 1) AddContent(_pickerItems[chosen[0]].Asset, _pickerItems[chosen[0]].Name, _pickerItems[chosen[0]].Category); };
        AddChild(_pickerDialog);
        return page;
    }

    private static PanelContainer PanelWith(StyleBox box) { var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; panel.AddThemeStyleboxOverride("panel", box); return panel; }

    private static StyleBoxFlat ContentRowBox(bool selected) =>
        selected ? UiStyle.Box(new Color(UiStyle.AccentFill, .55f), UiStyle.Accent, 12, 2, 10, 6) : UiStyle.Box(new Color(UiStyle.FieldFill, .30f), new Color(0, 0, 0, 0), 12, 0, 10, 6);

    private void SelectContent(int index)
    {
        _contentSelected = index;
        for (var i = 0; i < _contentRowNodes.Count; i++) _contentRowNodes[i].AddThemeStyleboxOverride("panel", ContentRowBox(i == index));
        if (index >= 0 && index < Entries().Count) _contentName.Text = Entries()[index]["name"]?.GetValue<string>() ?? "";
    }

    /// One line of the list: a small picture of the kind, the name, the kind in words and a button that removes the item.
    private void AddContentRow(int index, string name, string category)
    {
        var row = PanelWith(ContentRowBox(false)); row.MouseFilter = Control.MouseFilterEnum.Stop;
        var line = Row(12); row.AddChild(line);
        var thumb = new PanelContainer { CustomMinimumSize = new Vector2(66, 66) }; thumb.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color("#1D2A33"), UiStyle.Border, 10, 1, 0, 0));
        var glyph = UiStyle.IconRect(CategoryIcon(category), UiStyle.AccentText, 34); glyph.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter; thumb.AddChild(glyph); line.AddChild(thumb);
        var title = Txt(name, 22, true); title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; line.AddChild(title);
        var kind = Row(10); kind.CustomMinimumSize = new Vector2(250, 0); kind.AddChild(UiStyle.IconRect(CategoryIcon(category), UiStyle.Text, 26)); kind.AddChild(Txt(CategoryLabel(category), 21)); line.AddChild(kind);
        var trash = SquareIcon("trash", "Tira o item do objeto", () => { SelectContent(index); RemoveContent(); }); trash.CustomMinimumSize = new Vector2(62, 48); trash.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var actions = new CenterContainer { CustomMinimumSize = new Vector2(90, 0) }; actions.AddChild(trash); line.AddChild(actions);
        row.GuiInput += input => { if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) SelectContent(index); };
        _contentRowNodes.Add(row); _contentRows.AddChild(row);
    }
}
