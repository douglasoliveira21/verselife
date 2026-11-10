using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Scripts;

/// Recursos, Textura and Conteúdo tabs in the compact layout of the reference image (see BuildEditor.CompactPages.cs).
public partial class BuildEditor
{
    private CheckBox _physical = null!, _staticRadio = null!, _dynamicRadio = null!;
    private TextureRect _appliedThumb = null!;
    private AcceptDialog _libraryDialog = null!;

    // ---- Recursos -------------------------------------------------------------------------------------------

    private Control BuildResourcesTab()
    {
        var page = Column(8);
        page.AddChild(Heading("Estados do objeto"));
        _physMode = Pick("Estático", "Físico"); // the state itself; the check boxes and radios below drive it
        _physical = Check("Físico", "O objeto deve se mover por física (cair, ser empurrado); o mundo ainda não simula esses corpos"); _physTemporary = Check("Temporário", "Objeto passageiro: quase não pesa no orçamento da ilha");
        _ghost = Check("Fantasma (Phantom)", "Desativa a colisão sólida; o objeto continua visível");
        var group = new ButtonGroup(); _staticRadio = new CheckBox { Text = " Estático", ButtonGroup = group, ButtonPressed = true, FocusMode = Control.FocusModeEnum.None }; _dynamicRadio = new CheckBox { Text = " Dinâmico", ButtonGroup = group, FocusMode = Control.FocusModeEnum.None };
        foreach (var radio in new[] { _staticRadio, _dynamicRadio }) radio.AddThemeFontSizeOverride("font_size", 19);
        _physical.Toggled += on => { _physMode.Select(on ? 1 : 0); SyncPhysicsUi(); };
        _staticRadio.Pressed += () => { _physMode.Select(0); SyncPhysicsUi(); }; _dynamicRadio.Pressed += () => { _physMode.Select(1); SyncPhysicsUi(); };
        var states = Row(20); var left = Column(4); left.AddChild(_physical); left.AddChild(_physTemporary); left.AddChild(_ghost); states.AddChild(left);
        var right = Column(4); right.AddChild(_staticRadio); right.AddChild(_dynamicRadio); states.AddChild(right); page.AddChild(states);
        page.AddChild(Rule());

        page.AddChild(Heading("Luz"));
        _lightOn = Check("Emitir luz", "O objeto passa a iluminar o que está perto (até 16 luzes acesas por vez no seu jogo)");
        _lightColor = ColorButton(new Color("#FFE0A8"), 120); _lightEnergy = NewSlider(0, 8, .1, 1.5); _lightRange = NewSlider(.5, 30, .5, 8); LightFalloffSlider();
        var lightRow = Row(12); lightRow.AddChild(_lightOn); lightRow.AddChild(Filler()); lightRow.AddChild(Txt("Cor", 19)); lightRow.AddChild(_lightColor); page.AddChild(lightRow);
        page.AddChild(PairRow("Intensidade", ("", SpinFor(_lightEnergy)))); page.AddChild(PairRow("Alcance", ("", SpinFor(_lightRange)))); page.AddChild(PairRow("Atenuação", ("", SpinFor(_lightFalloff))));
        page.AddChild(BuildProjectorCard());
        page.AddChild(Rule());

        page.AddChild(BuildFlexCard());
        page.AddChild(Rule());

        page.AddChild(Heading("Física"));
        _collisionShape = new OptionButton { CustomMinimumSize = new Vector2(0, 40), FocusMode = Control.FocusModeEnum.None, TooltipText = "Padrão = caixa; o casco convexo e a malha seguem a forma" };
        _collisionShape.AddItem("Nenhuma"); _collisionShape.AddItem("Padrão (caixa)"); _collisionShape.AddItem("Casco convexo"); _collisionShape.AddItem("Malha triangular (estática)"); _collisionShape.Select(1);
        _collisionShape.ItemSelected += index => { if (index == 3 && _physMode.Selected == 1) { _physMode.Select(0); SyncPhysicsUi(); _status.Text = "Malha triangular é estática; o modo foi alterado para Estático."; } };
        page.AddChild(FormRow("Forma de colisão:", _collisionShape, 200));
        _physMass = Field("1", 8); _physMass.Text = "1"; _physFriction = Field("0,5", 6); _physFriction.Text = "0,5"; _physBounce = Field("0", 6); _physBounce.Text = "0";
        page.AddChild(FormRow("Massa:", _physMass, 200)); page.AddChild(FormRow("Atrito:", _physFriction, 200)); page.AddChild(FormRow("Restituição:", _physBounce, 200));
        _physMaterial = new OptionButton { CustomMinimumSize = new Vector2(0, 40), FocusMode = Control.FocusModeEnum.None, TooltipText = "Define o atrito e o quique de uma vez" }; _physMaterial.AddItem("Nenhum");
        foreach (var (key, (label, _, _)) in PhysicsRules.Materials) { _physMaterial.AddItem(label); _physMaterial.SetItemMetadata(_physMaterial.ItemCount - 1, key); }
        _physMaterial.ItemSelected += index => { if (index > 0 && PhysicsRules.Materials.TryGetValue(_physMaterial.GetItemMetadata((int)index).AsString(), out var m)) { _physFriction.Text = Disp(m.Friction, 2); _physBounce.Text = Disp(m.Bounce, 2); } };
        page.AddChild(FormRow("Material:", _physMaterial, 200));
        _showCollision = Check("Visualizar colisão", "Mostra em ciano a geometria que gera a colisão"); _showCollision.Toggled += SetCollisionPreview; page.AddChild(_showCollision);
        var apply = IconButton("check", "Aplicar", "Envia estados, luz, projetor, flexibilidade e física ao servidor", () => { ApplyLight(); ApplySelected(); }, UiStyle.ButtonKind.Primary, 44); page.AddChild(apply);
        return page;
    }

    /// Keeps the check box and the two radios in step with the physical mode.
    private void SyncPhysicsUi()
    {
        if (_physical is null) return;
        var on = _physMode.Selected == 1; _physical.SetPressedNoSignal(on); _staticRadio.SetPressedNoSignal(!on); _dynamicRadio.SetPressedNoSignal(on);
    }

    private VBoxContainer BuildProjectorCard()
    {
        var box = Column(6); box.AddChild(Txt("Projetor de luz (só com a luz ligada)", 18, true, UiStyle.TextMuted));
        _projTexture = new OptionButton { CustomMinimumSize = new Vector2(0, 40), FocusMode = Control.FocusModeEnum.None };
        _projTexture.AddItem("Nenhuma"); foreach (var (id, label) in BuiltInTextures.All) { _projTexture.AddItem(label); _projTexture.SetItemMetadata(_projTexture.ItemCount - 1, id); }
        _projAssetLabel = Txt(string.Empty, 16, false, UiStyle.TextMuted); _projTexture.ItemSelected += _ => { _projAsset = null; _projAssetLabel.Text = string.Empty; };
        box.AddChild(FormRow("Imagem:", _projTexture, 170));
        var library = TextButton("Usar a da biblioteca", "Usa a imagem escolhida na biblioteca da aba Textura", UseLibraryForProjector); box.AddChild(library); box.AddChild(_projAssetLabel);
        _projFov = NewSlider(10, 120, 1, 45); _projFocus = NewSlider(0, 30, .5, 10); _projAmbiance = NewSlider(0, 1, .01, .1);
        box.AddChild(PairRow("Campo de visão", ("", SpinFor(_projFov)))); box.AddChild(PairRow("Foco", ("", SpinFor(_projFocus)))); box.AddChild(PairRow("Ambiência", ("", SpinFor(_projAmbiance))));
        return box;
    }

    private VBoxContainer BuildFlexCard()
    {
        var box = Column(6); _flexOn = Check("Flexibilidade (Flexi)", "O objeto balança como uma bandeira ou uma planta: a base fica parada e o topo se inclina"); box.AddChild(_flexOn);
        HSlider Flex(string key, double min, double max, double value) { var slider = NewSlider(min, max, .1, value); _flexSliders[key] = slider; return slider; }
        var soft = Flex("softness", 0, 3, 1); var grav = Flex("gravity", -10, 10, 0); var drag = Flex("drag", 0, 10, 2); var wind = Flex("wind", 0, 10, 0); var tension = Flex("tension", 0, 10, 1);
        var fx = Flex("forceX", -10, 10, 0); var fy = Flex("forceY", -10, 10, 0); var fz = Flex("forceZ", -10, 10, 0);
        box.AddChild(PairRow("Maciez", ("", SpinFor(soft)))); box.AddChild(PairRow("Gravidade", ("", SpinFor(grav)))); box.AddChild(PairRow("Arrasto", ("", SpinFor(drag)))); box.AddChild(PairRow("Vento", ("", SpinFor(wind)))); box.AddChild(PairRow("Tensão", ("", SpinFor(tension))));
        box.AddChild(PairRow("Força", ("X", SpinFor(fx)), ("Y", SpinFor(fy)), ("Z", SpinFor(fz))));
        return box;
    }

    // ---- Textura --------------------------------------------------------------------------------------------

    private Control BuildTextureTab()
    {
        var page = Column(8);
        _target = Txt("Objeto inteiro", 19, true, UiStyle.AccentText);
        _facePick = new OptionButton { CustomMinimumSize = new Vector2(0, 40), FocusMode = Control.FocusModeEnum.None, TooltipText = "Escolhe a face que recebe as mudanças; Shift + clique no mundo (ferramenta Face) soma mais faces" };
        _facePick.ItemSelected += index => OnFacePicked((int)index);
        page.AddChild(FormRow("Selecionar faces:", _facePick, 200));
        _faceLabel = Txt("Sem face escolhida: vale o objeto inteiro.", 16, false, UiStyle.TextMuted); _faceLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart; page.AddChild(_faceLabel);

        var top = Row(12);
        _appliedThumb = new TextureRect { CustomMinimumSize = new Vector2(150, 150), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, ClipContents = true };
        var frame = new PanelContainer(); frame.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color("#121A20"), UiStyle.Border, 8, 1, 3, 3)); frame.AddChild(_appliedThumb); top.AddChild(frame);
        var side = Column(6); top.AddChild(side);
        _appliedLabel = Txt("nenhuma", 19, true, UiStyle.AccentText); _appliedLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart; side.AddChild(Txt("Textura aplicada:", 17, false, UiStyle.TextMuted)); side.AddChild(_appliedLabel);
        side.AddChild(TextButton("Escolher…", "Abre a biblioteca: suas texturas do inventário, favoritas, recentes e as do sistema", OpenLibrary));
        side.AddChild(TextButton("Limpar", "Retira a textura e a cor do alvo: o objeto fica liso e branco", RemoveTexture));
        side.AddChild(TextButton("Aplicar a todas", "Usa a textura escolhida em todas as faces do objeto", ApplyTextureToAll));
        page.AddChild(top);

        _colorPicker = ColorButton(new Color("#A8C7D8"), 120); _colorPicker.TooltipText = "Cor do objeto (a textura é pintada por cima)"; _colorPicker.PopupClosed += ScheduleMaterialApply;
        var colourRow = Row(10); colourRow.AddChild(_colorPicker); colourRow.AddChild(Swatches(QuickColors, color => { _colorPicker.Color = color; ScheduleMaterialApply(); }, 26)); page.AddChild(FormRow("Cor:", colourRow, 100));
        _roughness = NewSlider(0, 1, .01, .7); _metallic = NewSlider(0, 1, .01, 0); _opacity = NewSlider(0, 1, .01, 1);
        page.AddChild(PairRow("Transparência %", ("", SpinFor(_opacity, ScheduleMaterialApply, percentInverted: true))));
        page.AddChild(Rule());

        AddMapsSection(page);
        page.AddChild(Heading("Repetição (Tiling)"));
        _repeatX = Field("1", 8); _repeatY = Field("1", 8); _offsetX = Field("0", 8); _offsetY = Field("0", 8); _uvRotation = Field("0", 8);
        foreach (var (field, text) in new[] { (_repeatX, "1"), (_repeatY, "1"), (_offsetX, "0"), (_offsetY, "0"), (_uvRotation, "0") }) { field.Text = text; field.CustomMinimumSize = new Vector2(0, 40); field.TextSubmitted += _ => { if (!_filling) ApplyUv(); }; field.FocusExited += () => { if (!_filling) ApplyUv(); }; }
        page.AddChild(PairRow("Repetição", ("Horizontal (U)", _repeatX), ("Vertical (V)", _repeatY)));
        page.AddChild(Heading("Offset")); page.AddChild(PairRow("Deslocamento", ("U", _offsetX), ("V", _offsetY)));
        page.AddChild(FormRow("Rotação (°):", _uvRotation, 200));
        _flipU = Check("Inverter horizontal"); _flipV = Check("Inverter vertical"); _flipU.Toggled += _ => { if (!_filling) ApplyUv(); }; _flipV.Toggled += _ => { if (!_filling) ApplyUv(); };
        page.AddChild(_flipU); page.AddChild(_flipV);
        page.AddChild(Rule());

        page.AddChild(Heading("Material"));
        _glow = Check("Brilho (emissão)", "O material passa a emitir luz própria"); _glow.Toggled += _ => ScheduleMaterialApply(); _emissionPicker = ColorButton(Colors.White, 100); _emissionPicker.PopupClosed += ScheduleMaterialApply;
        _glowStrength = NewSlider(0, 1, .01, .125);
        var glowRow = Row(12); glowRow.AddChild(_glow); glowRow.AddChild(Filler()); glowRow.AddChild(_emissionPicker); page.AddChild(glowRow);
        page.AddChild(PairRow("Intensidade", ("", SpinFor(_glowStrength, ScheduleMaterialApply))));
        page.AddChild(PairRow("Rugosidade", ("", SpinFor(_roughness, ScheduleMaterialApply)))); page.AddChild(PairRow("Metalicidade", ("", SpinFor(_metallic, ScheduleMaterialApply))));
        page.AddChild(BuildAdvancedMaterialCard());
        var props = Row(8); props.AddChild(IconButton("copy", "Copiar propriedades", "Copia material e forma do objeto selecionado", CopyProperties, UiStyle.ButtonKind.Secondary, 40)); props.AddChild(IconButton("paste", "Colar propriedades", "Aplica material e forma copiados à seleção", PasteProperties, UiStyle.ButtonKind.Secondary, 40)); page.AddChild(props);
        BuildLibraryDialog();
        return page;
    }

    private void OnFacePicked(int index)
    {
        var view = _selection.Count == 1 ? _selection[0] : null;
        if (view is null || !view.IsProcedural) return;
        if (index == 0) { ClearFace(); RefreshTargetCard(); return; }
        var id = _facePick.GetItemMetadata(index).AsInt32(); if (id < 0) return; // "several faces" is only informative
        SetFaceSelection(view, [id]);
    }

    /// Fills the face list of the Textura tab for the selected object and shows the chosen face(s).
    private void RefreshTargetCard()
    {
        if (_facePick is null) return;
        var view = _selection.Count == 1 ? _selection[0] : null;
        var key = view is null || !view.IsProcedural ? string.Empty : view.State.Id + "|" + view.State.PrimitiveType + "|" + string.Join(",", view.FaceIds);
        if (key != _targetKey)
        {
            _targetKey = key; _facePick.Clear(); _facePick.AddItem("Todas as faces"); _facePick.SetItemMetadata(0, -1);
            if (view is not null && view.IsProcedural)
            {
                var number = 0;
                foreach (var id in view.FaceIds) { number++; _facePick.AddItem($"Face {number} · {FaceName(view.State.PrimitiveType, id)}"); _facePick.SetItemMetadata(_facePick.ItemCount - 1, id); }
            }
        }
        var selected = 0;
        if (view is not null && _faceView == view && _faceSet.Count > 0)
        {
            if (_faceSet.Count == 1) { for (var i = 1; i < _facePick.ItemCount; i++) if (_facePick.GetItemMetadata(i).AsInt32() == _faceSet.First()) selected = i; }
            else { _facePick.AddItem($"{_faceSet.Count} faces escolhidas"); _facePick.SetItemMetadata(_facePick.ItemCount - 1, -1); selected = _facePick.ItemCount - 1; _targetKey = string.Empty; }
        }
        _facePick.Select(selected);
    }

    public IReadOnlyList<string> FaceChoicesForTesting => [.. Enumerable.Range(0, _facePick.ItemCount).Select(_facePick.GetItemText)];
    public void PickFaceForTesting(int index) { _facePick.Select(index); OnFacePicked(index); }
    public int SelectedFaceIndexForTesting => _facePick.Selected;

    private void OpenLibrary() { RefreshLibrary(); _libraryDialog.PopupCentered(new Vector2I(940, 560)); }

    /// The texture library as a window of its own (sources, the images, a preview, import): opened by "Escolher…".
    private void BuildLibraryDialog()
    {
        _libraryDialog = new AcceptDialog { Title = "Escolher textura", OkButtonText = "Aplicar ao alvo", Exclusive = false, MinSize = new Vector2I(900, 520) };
        var asNormal = _libraryDialog.AddButton("Como mapa normal", false, "normal"); _libraryDialog.CustomAction += action => { if (action == "normal") ApplyTexture(normal: true); };
        _libraryDialog.Confirmed += () => ApplyTexture(normal: false);
        var content = new VBoxContainer(); content.AddThemeConstantOverride("separation", 8); _libraryDialog.AddChild(content);
        var header = Row(10); _libraryStatus = Txt(string.Empty, 16, false, UiStyle.TextMuted); _libraryStatus.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; _libraryStatus.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; header.AddChild(_libraryStatus);
        var import = OrangeButton("download", "Importar imagem", "Envia uma imagem (PNG, JPG ou WebP) ao seu inventário como textura", () => _imageDialog.PopupCentered(new Vector2I(800, 500)), 40); header.AddChild(import); content.AddChild(header);
        var row = Row(12); row.SizeFlagsVertical = Control.SizeFlags.ExpandFill; content.AddChild(row);
        var sources = Column(6); sources.CustomMinimumSize = new Vector2(210, 0); row.AddChild(sources);
        var sourceNames = new[] { ("folder", "Minhas texturas"), ("star", "Favoritas"), ("history", "Recentes"), ("layers", "Texturas do sistema") };
        for (var i = 0; i < sourceNames.Length; i++)
        {
            var index = i; var button = ToolToggle(" " + sourceNames[i].Item2, "Mostra " + sourceNames[i].Item2.ToLowerInvariant(), () => SelectSource(index));
            button.CustomMinimumSize = new Vector2(0, 38); button.Icon = UiStyle.Icon(sourceNames[i].Item1, UiStyle.Text, 22); button.ExpandIcon = false; button.Alignment = HorizontalAlignment.Left; button.AddThemeFontSizeOverride("font_size", 17);
            _sourceButtons.Add(button); sources.AddChild(button);
        }
        _library = new ItemList { MaxColumns = 4, SameColumnWidth = true, IconMode = ItemList.IconModeEnum.Top, FixedIconSize = new Vector2I(96, 76), SelectMode = ItemList.SelectModeEnum.Single, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, FixedColumnWidth = 110 };
        _library.ItemSelected += index => ShowPreview((int)index); row.AddChild(_library);
        var previewBox = Column(6); previewBox.CustomMinimumSize = new Vector2(250, 0); row.AddChild(previewBox);
        _preview = new TextureRect { CustomMinimumSize = new Vector2(240, 200), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, ClipContents = true }; previewBox.AddChild(_preview);
        _previewName = Txt("—", 18, true); previewBox.AddChild(_previewName); _previewInfo = Txt(string.Empty, 16, false, UiStyle.TextMuted); previewBox.AddChild(_previewInfo);
        AddChild(_libraryDialog);
        _imageDialog = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = ["*.png,*.jpg,*.jpeg,*.webp ; Imagens"], Title = "Importar textura" };
        _imageDialog.FileSelected += ImportImage; AddChild(_imageDialog);
        _sourceButtons[0].SetPressedNoSignal(true);
    }

    /// The thumbnail and name of the texture the target uses now.
    private void ShowAppliedThumb(JsonObject layer)
    {
        if (_appliedThumb is null) return;
        _appliedThumb.Texture = null;
        if (layer["builtin"]?.GetValue<string>() is { } builtin) _appliedThumb.Texture = BuiltInTextures.Get(builtin);
        else if (layer["texture"]?.GetValue<string>() is { } asset && Guid.TryParse(asset, out var id)) Textures.Request(id, texture => { if (IsInstanceValid(_appliedThumb)) _appliedThumb.Texture = texture; });
    }

    // ---- Conteúdo -------------------------------------------------------------------------------------------

    private Control BuildContentTab()
    {
        var page = Column(8);
        var head = Row(10); _contentCount = Txt("0 itens", 19, true, UiStyle.AccentText); head.AddChild(Heading("Conteúdo do objeto")); head.AddChild(Filler()); head.AddChild(_contentCount); page.AddChild(head);
        InitScripting();
        var insertRow = Row(8); var newScript = OrangeButton("code", "Novo script…", "Cria um script Lua dentro do objeto e abre o editor", () => _ = NewScriptAsync(), 44); newScript.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; insertRow.AddChild(newScript);
        var insert = OrangeButton("plus", "Inserir do inventário…", "Escolhe um item do seu inventário para guardar no objeto; também dá para arrastar do inventário para cá", OpenPicker, 44); insert.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; insertRow.AddChild(insert); page.AddChild(insertRow);
        var headers = Row(8); var n = Txt("Nome", 18, true); n.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; headers.AddChild(n); var t = Txt("Tipo", 18, true); t.CustomMinimumSize = new Vector2(190, 0); headers.AddChild(t); var p = Txt("Dir.", 18, true); p.CustomMinimumSize = new Vector2(80, 0); headers.AddChild(p); page.AddChild(headers);
        _contentRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 120) }; _contentRows.AddThemeConstantOverride("separation", 3); page.AddChild(_contentRows); EnableContentDrop(_contentRows); EnableContentDrop(page);
        _contentInfo = Txt("Selecione um objeto.", 16, false, UiStyle.TextMuted); page.AddChild(_contentInfo);
        var actions = Row(8);
        foreach (var (text, tip, action) in new (string, string, string)[] { ("Abrir", "Pré-visualiza o item selecionado", "open"), ("Propriedades…", "Direitos do item selecionado", "properties"), ("Transferir…", "Oferece o item a outra pessoa", "transfer") })
        { var button = TextButton(text, tip, () => RunSelectedContent(action)); button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; actions.AddChild(button); }
        var remove = TextButton("Remover", "Tira o item do objeto (ele continua no seu inventário); um script é apagado", RemoveSelectedContent); remove.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; actions.AddChild(remove); page.AddChild(actions);
        var copy = TextButton("Copiar para o inventário", "Faz uma cópia do item selecionado no seu inventário (se ele permitir cópia)", () => RunSelectedContent("copy")); page.AddChild(copy);
        _contentName = Field("Novo nome", 120); _contentName.TextSubmitted += _ => RenameContent();
        var rename = Row(8); rename.AddChild(_contentName); var renameButton = TextButton("Renomear", "Muda o nome do item ou do script selecionado dentro do objeto", () => { if (_scriptSelected != Guid.Empty) _ = RenameSelectedScriptAsync(_contentName.Text); else RenameContent(); }); rename.AddChild(renameButton); page.AddChild(rename);
        page.AddChild(NoteBar("Dica:", "Adicione sons, texturas, modelos 3D e outros recursos do seu inventário para compor este objeto."));
        _pickerDialog = new AcceptDialog { Title = "Inserir no objeto", OkButtonText = "Inserir", Exclusive = true, MinSize = new Vector2I(420, 420) };
        _picker = new ItemList { CustomMinimumSize = new Vector2(380, 320), SizeFlagsVertical = Control.SizeFlags.ExpandFill }; _pickerDialog.AddChild(_picker);
        _pickerDialog.Confirmed += () => { var chosen = _picker.GetSelectedItems(); if (chosen.Length == 1) AddContent(_pickerItems[chosen[0]].Asset, _pickerItems[chosen[0]].Name, _pickerItems[chosen[0]].Category); };
        AddChild(_pickerDialog);
        return page;
    }

    private void RunSelectedContent(string action)
    {
        if (_scriptSelected != Guid.Empty) { if (action is "open" or "properties") _ = OpenSelectedScriptAsync(); else _status.Text = "Um script fica dentro do objeto: copie ou transfira o objeto inteiro."; return; }
        var entries = Entries(); if (_contentSelected < 0 || _contentSelected >= entries.Count) { _status.Text = "Escolha um item da lista."; return; }
        if (Guid.TryParse(entries[_contentSelected]["assetId"]?.GetValue<string>(), out var asset)) RunContentAction(asset, action);
    }

    /// One line of the list: the name, the kind and the rights; selecting it shows its name in the rename box and enables the buttons below.
    private void AddContentRow(int index, string name, string category, Guid asset, string rights)
    {
        var row = PanelWith(ContentRowBox(false)); row.MouseFilter = Control.MouseFilterEnum.Stop;
        var line = Row(8); row.AddChild(line);
        line.AddChild(UiStyle.IconRect(CategoryIcon(category), UiStyle.AccentText, 24));
        var title = Txt(name, 19, true); title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; line.AddChild(title);
        var kind = Txt(CategoryLabel(category), 18); kind.CustomMinimumSize = new Vector2(190, 0); line.AddChild(kind);
        var rightsLabel = Txt(rights, 18, true, UiStyle.AccentText); rightsLabel.CustomMinimumSize = new Vector2(80, 0); rightsLabel.TooltipText = "O que você pode fazer com este item: C copiar, M modificar, T transferir, R revender"; line.AddChild(rightsLabel);
        row.GuiInput += input => { if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) SelectContent(index); };
        _contentRowNodes.Add(row); _contentRows.AddChild(row);
    }
}
