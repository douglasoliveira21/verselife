using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts.IslandManagement;
using LifeVerse.Client.Scripts.LandManagement;
using LifeVerse.Shared.Groups;

namespace LifeVerse.Client.Scripts.Groups;

/// <summary>
/// "Criar grupo" in four steps (Informações, Entrada, Configurações, Confirmação). Each step is checked before moving on; the server checks everything again when the group is created.
/// Creating a group costs nothing, so the last step says so instead of showing a price.
/// </summary>
public sealed partial class GroupCreateWindow : Node
{
    public static readonly string[] Steps = ["Informações", "Entrada", "Configurações", "Confirmação"];
    public static readonly (string Id, string Text)[] CategoryOptions =
        [("social", "Social"), ("construcao", "Construção"), ("comercio", "Comércio"), ("jogos", "Jogos"), ("educacao", "Educação"), ("musica", "Música"), ("arte", "Arte"), ("eventos", "Eventos"), ("outros", "Outros")];
    public static readonly (string Id, string Text)[] LanguageOptions = [("pt-BR", "Português (Brasil)"), ("en", "English"), ("es", "Español")];

    private FloatingWindow _window = null!;
    private VBoxContainer _body = null!;
    private Label _note = null!;
    private Button _back = null!, _next = null!;
    private readonly List<Label> _stepLabels = [];
    private Func<string, string, string?, Task<(long, string)>>? _transport;
    private GroupProfileApi? _api;
    private int _step;
    private bool _busy;

    // what has been typed so far (kept here so going back and forth loses nothing)
    private string _name = "", _tag = "", _description = "", _category = "social", _language = "pt-BR", _privacy = "public", _policy = "request", _rating = "livre", _cover = "";
    private long _fee;
    private int _limit = GroupRules.DefaultMemberLimit;

    /// <summary>The group was created: its id.</summary>
    public Action<Guid>? Created { get; set; }
    public Action<string>? Said { get; set; }

    public bool Visible => _window.Visible;
    public int StepForTesting => _step;
    public string NoteForTesting => _note.Text;
    public Vector2 SizeForTesting => _window.Size;
    public string TextForTesting => string.Join("|", _body.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text));
    public Control BodyForTesting => _body;
    public bool ButtonEnabledForTesting(string text) => _window.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text.Trim() == text && b.Visible) is { Disabled: false };
    public bool HasButtonForTesting(string text) => _window.FindChildren("*", "Button", true, false).OfType<Button>().Any(b => b.Text.Trim() == text && b.Visible);
    public void PressForTesting(string text) { if (_window.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text.Trim() == text && b.Visible) is { } b) b.EmitSignal(BaseButton.SignalName.Pressed); }
    public void SetTransportForTesting(Func<string, string, string?, Task<(long, string)>> transport) { _transport = transport; _api = new GroupProfileApi(new IslandManagementApi(transport)); }
    private GroupProfileApi Api => _api ??= new GroupProfileApi(new IslandManagementApi(_transport ?? ScriptApi.HttpTransport(this)));

    public override void _Ready() => Build();

    public void Open()
    {
        _step = 0; _name = _tag = _description = _cover = ""; _category = "social"; _language = "pt-BR"; _privacy = "public"; _policy = "request"; _rating = "livre"; _fee = 0; _limit = GroupRules.DefaultMemberLimit; _note.Text = "";
        _window.Visible = true; _window.MoveToFront(); Render();
    }

    private void Build()
    {
        _window = new FloatingWindow("Criar grupo", icon: "people", minimizable: false) { Visible = false }; _window.Place(.5f, .5f, -280, -300, 280, 300); _window.CloseRequested += () => _window.Visible = false; AddChild(_window);
        var column = IslandUi.Column(10); column.SizeFlagsVertical = Control.SizeFlags.ExpandFill; _window.Content.AddChild(column);
        var steps = IslandUi.Row(8); column.AddChild(steps);
        for (var i = 0; i < Steps.Length; i++) { var label = new Label { Text = $"{i + 1}. {Steps[i]}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Center }; label.AddThemeFontSizeOverride("font_size", 13); steps.AddChild(label); _stepLabels.Add(label); }
        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) }; _note.AddThemeColorOverride("font_color", UiStyle.Warning); column.AddChild(_note);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(480, 330) }; column.AddChild(scroll);
        _body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _body.AddThemeConstantOverride("separation", 8); scroll.AddChild(_body);
        var buttons = IslandUi.Row(8); column.AddChild(buttons);
        _back = IslandUi.Action("Voltar", null, IslandUi.Kind.Secondary, 38, 16); _back.Pressed += () => { if (_step > 0 && !_busy) { _step--; _note.Text = ""; Render(); } }; buttons.AddChild(_back);
        buttons.AddChild(IslandUi.Spacer());
        var cancel = IslandUi.Action("Cancelar", null, IslandUi.Kind.Quiet, 38, 16); cancel.Pressed += () => _window.Visible = false; buttons.AddChild(cancel);
        _next = IslandUi.Action("Próximo", null, IslandUi.Kind.Primary, 38, 16); _next.Pressed += OnNext; buttons.AddChild(_next);
    }

    private Label Hint(string text) { var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(240, 0) }; label.AddThemeFontSizeOverride("font_size", 12); label.AddThemeColorOverride("font_color", UiStyle.TextMuted); return label; }
    private Label Caption(string text) { var label = new Label { Text = text }; label.AddThemeFontSizeOverride("font_size", 13); label.AddThemeFontOverride("font", UiStyle.Medium); return label; }
    private static string Money(long value) => "L$ " + value.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
    private static string Policy(string policy) => policy switch { "open" => "Entrada livre", "invite" => "Só por convite", _ => "Entrada por aprovação" };
    private static string Text(IEnumerable<(string Id, string Text)> options, string id) => options.FirstOrDefault(o => o.Id == id).Text ?? id;

    private void Render()
    {
        foreach (var child in _body.GetChildren()) child.QueueFree();
        for (var i = 0; i < _stepLabels.Count; i++) { _stepLabels[i].AddThemeColorOverride("font_color", i == _step ? UiStyle.Accent : i < _step ? UiStyle.Text : UiStyle.TextMuted); }
        _back.Visible = _step > 0; _next.Text = _step == Steps.Length - 1 ? "Criar grupo" : "Próximo";
        switch (_step)
        {
            case 0: StepInfo(); break;
            case 1: StepEntry(); break;
            case 2: StepSettings(); break;
            default: StepConfirm(); break;
        }
    }

    private void StepInfo()
    {
        _body.AddChild(Caption("Nome do grupo")); var name = IslandUi.Input("2 a 40 caracteres", 40); name.Text = _name; name.TextChanged += t => _name = t; name.Name = "name"; _body.AddChild(name);
        _body.AddChild(Caption("Sigla (opcional)")); var tag = IslandUi.Input("2 a 5 letras ou números", 5); tag.Text = _tag; tag.TextChanged += t => _tag = t; tag.Name = "tag"; _body.AddChild(tag);
        _body.AddChild(Caption("Descrição")); var about = IslandUi.TextArea("Conte o que o grupo faz", 90); about.Text = _description; about.TextChanged += () => _description = about.Text; about.Name = "description"; _body.AddChild(about);
        var row = IslandUi.Row(10); _body.AddChild(row);
        var left = IslandUi.Column(4); left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(left); left.AddChild(Caption("Categoria"));
        var category = IslandUi.Dropdown(CategoryOptions.Select(c => (c.Text, c.Id))); IslandUi.Select(category, _category); category.ItemSelected += _ => _category = IslandUi.IdOf(category); category.Name = "category"; left.AddChild(category);
        var right = IslandUi.Column(4); right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(right); right.AddChild(Caption("Idioma"));
        var language = IslandUi.Dropdown(LanguageOptions.Select(c => (c.Text, c.Id))); IslandUi.Select(language, _language); language.ItemSelected += _ => _language = IslandUi.IdOf(language); language.Name = "language"; right.AddChild(language);
    }

    private void StepEntry()
    {
        _body.AddChild(Caption("Quem vê o grupo")); var privacy = IslandUi.Dropdown([("Público: aparece na busca", "public"), ("Privado: só por convite", "private")]); IslandUi.Select(privacy, _privacy); privacy.Name = "privacy"; _body.AddChild(privacy);
        _body.AddChild(Caption("Como se entra")); var policy = IslandUi.Dropdown([("Entrada livre", "open"), ("Entrada por aprovação", "request"), ("Só por convite", "invite")]); IslandUi.Select(policy, _policy); policy.Name = "policy"; _body.AddChild(policy);
        var feeBox = IslandUi.Column(4); _body.AddChild(feeBox); feeBox.AddChild(Caption("Taxa de entrada (L$)")); var fee = IslandUi.Number(0, GroupRules.MaxJoinFee, 1, " L$"); fee.Value = _fee; fee.ValueChanged += v => _fee = (long)v; fee.Name = "fee"; feeBox.AddChild(fee);
        var feeHint = Hint("A taxa só vale para grupos de entrada livre: quem entra paga e o valor vai para o cofre do grupo."); feeBox.AddChild(feeHint);
        void Sync()
        {
            _privacy = IslandUi.IdOf(privacy); _policy = IslandUi.IdOf(policy);
            if (_privacy == "private") { _policy = "invite"; IslandUi.Select(policy, "invite"); }
            policy.Disabled = _privacy == "private"; feeBox.Visible = _policy == "open"; if (_policy != "open") { _fee = 0; fee.Value = 0; }
        }
        privacy.ItemSelected += _ => Sync(); policy.ItemSelected += _ => Sync(); Sync();
        _body.AddChild(Caption("Limite de membros")); var limit = IslandUi.Number(2, GroupRules.MaxMemberLimit, 1); limit.Value = _limit; limit.ValueChanged += v => _limit = (int)v; limit.Name = "limit"; _body.AddChild(limit);
    }

    private void StepSettings()
    {
        _body.AddChild(Caption("Classificação de conteúdo")); var rating = IslandUi.Dropdown([("Livre", "livre"), ("Moderado", "moderado"), ("Adulto", "adulto")]); IslandUi.Select(rating, _rating); rating.ItemSelected += _ => _rating = IslandUi.IdOf(rating); rating.Name = "rating"; _body.AddChild(rating);
        _body.AddChild(Caption("Imagem de capa (opcional)")); var cover = IslandUi.Input("https://…", 400); cover.Text = _cover; cover.TextChanged += t => _cover = t; cover.Name = "cover"; _body.AddChild(cover);
        _body.AddChild(Hint("O cargo, as permissões, os avisos e as propostas ficam no perfil do grupo, depois de criado. Quem cria o grupo é o líder e ocupa o cargo mais alto."));
    }

    private void StepConfirm()
    {
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; card.AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(UiStyle.RowFill, .55f), UiStyle.BorderSoft, 12, 1, 14, 10)); _body.AddChild(card);
        var lines = IslandUi.Column(4); card.AddChild(lines);
        lines.AddChild(Caption(_tag.Length > 0 ? $"{_name.Trim()}  [{_tag.ToUpperInvariant()}]" : _name.Trim()));
        if (_description.Trim().Length > 0) lines.AddChild(Hint(_description.Trim()));
        lines.AddChild(Hint($"Categoria: {Text(CategoryOptions, _category)} · Idioma: {Text(LanguageOptions, _language)}"));
        lines.AddChild(Hint($"{(_privacy == "private" ? "Privado" : "Público")} · {Policy(_policy)}{(_policy == "open" && _fee > 0 ? $" · taxa {Money(_fee)}" : _policy == "open" ? " · grátis" : "")}"));
        lines.AddChild(Hint($"Até {_limit} membros · Conteúdo {_rating}"));
        _body.AddChild(Hint("Criar um grupo é gratuito. Você será o líder e poderá mudar tudo isso depois, no perfil do grupo."));
    }

    /// <summary>Why the step cannot be left yet, or empty.</summary>
    private string Problem()
    {
        switch (_step)
        {
            case 0:
                var name = _name.Trim();
                if (name.Length < 2 || name.Length > 40) return "O nome precisa ter de 2 a 40 caracteres.";
                if (_tag.Length > 0 && !GroupRules.ValidTag(_tag)) return "A sigla precisa ter de 2 a 5 letras ou números.";
                if (_description.Length > 600) return "A descrição pode ter até 600 caracteres.";
                return "";
            case 1:
                return _fee < 0 || _fee > GroupRules.MaxJoinFee ? "A taxa vai de L$ 0 a L$ 1.000.000." : "";
            case 2:
                return _cover.Length > 0 && !_cover.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "A capa precisa ser um endereço https." : "";
            default: return "";
        }
    }

    private async void OnNext()
    {
        if (_busy) return; var problem = Problem(); _note.Text = problem; if (problem.Length > 0) return;
        if (_step < Steps.Length - 1) { _step++; Render(); return; }
        _busy = true; _next.Disabled = true;
        var reply = await Api.Create(new { name = _name.Trim(), description = _description.Trim(), tag = _tag.Length == 0 ? null : _tag.ToUpperInvariant(), category = _category, language = _language, privacy = _privacy, joinPolicy = _policy, joinFee = _policy == "open" ? _fee : 0, memberLimit = _limit, contentRating = _rating, coverUrl = _cover });
        _busy = false; _next.Disabled = false;
        if (!reply.Ok) { _note.Text = GroupProfileWindow.GroupWords(reply); return; }
        var id = reply.Body.ValueKind == JsonValueKind.Object && reply.Body.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String && Guid.TryParse(i.GetString(), out var parsed) ? parsed : Guid.Empty;
        _window.Visible = false; Said?.Invoke($"Grupo “{_name.Trim()}” criado."); Created?.Invoke(id);
    }
}
