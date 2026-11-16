using System.Globalization;
using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The tab Feed: the publications of the person (a short text, maybe one picture of their gallery, for everybody or only friends) and the achievements they unlocked, newest first, loaded a page at a time.
/// The server decides what this viewer may see; the tab only shows loading, empty, hidden and error states honestly.
/// </summary>
public partial class ProfileWindow
{
    private const int MaxPostText = 500;

    private VBoxContainer _feedRows = null!;
    private Control _composer = null!;
    private TextEdit _postText = null!;
    private OptionButton _postVisibility = null!;
    private CheckBox _postAttach = null!;
    private Label _postNote = null!, _postCount = null!, _feedState = null!;
    private Button _publish = null!, _feedMore = null!, _feedRetry = null!;
    private DateTimeOffset? _feedCursor;
    private int _feedFor;
    private bool _feedLoading, _feedHasMore;

    // ---- test access -------------------------------------------------------------------------------------------------------
    public int FeedRowsForTesting => _feedRows.GetChildren().OfType<PanelContainer>().Count();
    public string FeedStateForTesting => _feedState.Visible ? _feedState.Text : "";
    public bool ComposerVisibleForTesting => _composer.Visible;
    public bool FeedMoreVisibleForTesting => _feedMore.Visible;
    public bool FeedRetryVisibleForTesting => _feedRetry.Visible;
    public string FeedRowTextForTesting(int index) => string.Join(" ", _feedRows.GetChildren().OfType<PanelContainer>().ElementAt(index).FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text));
    public void PressFeedMoreForTesting() => _feedMore.EmitSignal(BaseButton.SignalName.Pressed);
    public void PressFeedRetryForTesting() => _feedRetry.EmitSignal(BaseButton.SignalName.Pressed);
    public void PressDeletePostForTesting(int index) => _feedRows.GetChildren().OfType<PanelContainer>().ElementAt(index).FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Name == "DeletePost").EmitSignal(BaseButton.SignalName.Pressed);
    public Task PublishForTesting(string text, bool friendsOnly, bool attach) { _postText.Text = text; _postVisibility.Select(friendsOnly ? 1 : 0); _postAttach.ButtonPressed = attach; return PublishAsync(); }

    private Control BuildFeedTab()
    {
        var inner = new VBoxContainer(); inner.AddThemeConstantOverride("separation", 10);
        inner.AddChild(Text(Heading("Feed"), 15, true, UiStyle.AccentText));

        var composer = new VBoxContainer { Name = "Composer" }; composer.AddThemeConstantOverride("separation", 6); _composer = composer; inner.AddChild(composer);
        _postText = new TextEdit { Name = "PostText", PlaceholderText = "O que está acontecendo no seu mundo?", WrapMode = TextEdit.LineWrappingMode.Boundary, CustomMinimumSize = new Vector2(0, 84), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _postText.AddThemeFontSizeOverride("font_size", 16);
        _postText.TextChanged += () => { if (_postText.Text.Length > MaxPostText) { _postText.Text = _postText.Text[..MaxPostText]; _postText.SetCaretColumn(MaxPostText); } _postCount.Text = $"{_postText.Text.Length}/{MaxPostText}"; };
        composer.AddChild(_postText);
        var tools = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; tools.AddThemeConstantOverride("h_separation", 10); tools.AddThemeConstantOverride("v_separation", 6); composer.AddChild(tools);
        _postVisibility = new OptionButton { Name = "PostVisibility", FocusMode = Control.FocusModeEnum.None, TooltipText = "Quem pode ver esta publicação", CustomMinimumSize = new Vector2(150, 36) };
        _postVisibility.AddItem("Todos", 0); _postVisibility.AddItem("Só amigos", 1); tools.AddChild(_postVisibility);
        _postAttach = new CheckBox { Name = "PostAttach", Text = " Incluir a imagem selecionada", FocusMode = Control.FocusModeEnum.None, TooltipText = "Anexa a imagem da sua galeria que está em destaque" }; tools.AddChild(_postAttach);
        _postCount = Text($"0/{MaxPostText}", 13, false, UiStyle.TextMuted); _postCount.AutowrapMode = TextServer.AutowrapMode.Off; _postCount.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; tools.AddChild(_postCount);
        _publish = new Button { Name = "Publish", Text = "Publicar", Icon = UiStyle.Icon("send", UiStyle.Text, 18), FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 36), TooltipText = "Publica no seu perfil" }; UiStyle.StyleButton(_publish, UiStyle.ButtonKind.Primary, 14, 5, 12);
        _publish.Pressed += () => _ = PublishAsync(); tools.AddChild(_publish);
        _postNote = Text("", 14, false, UiStyle.Orange); composer.AddChild(_postNote);

        _feedRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _feedRows.AddThemeConstantOverride("separation", 8); inner.AddChild(_feedRows);
        _feedState = Muted(""); inner.AddChild(_feedState);
        _feedRetry = SmallButton("Tentar de novo", "Carrega o feed outra vez", () => _ = LoadFeedAsync(true)); _feedRetry.Visible = false; _feedRetry.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; inner.AddChild(_feedRetry);
        _feedMore = SmallButton("Carregar mais", "Mostra publicações mais antigas", () => _ = LoadFeedAsync(false)); _feedMore.Visible = false; _feedMore.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; inner.AddChild(_feedMore);
        return Card(inner);
    }

    /// <summary>Only the owner has the box to write; the attach option needs a picture in the gallery.</summary>
    private void FillFeedComposer()
    {
        if (_composer is null) return;
        _composer.Visible = IsSelf; _postAttach.Disabled = _gallery.Count == 0; if (_gallery.Count == 0) _postAttach.ButtonPressed = false;
        _postAttach.TooltipText = _gallery.Count == 0 ? "Adicione uma imagem à galeria para poder anexá-la" : "Anexa a imagem da sua galeria que está em destaque";
    }

    private void SetFeedState(string text, bool retry = false) { _feedState.Text = text; _feedState.Visible = text.Length > 0; _feedRetry.Visible = retry; }

    /// <summary>Loads the feed of the profile on screen: the newest page, or (<paramref name="reset"/> false) the next older one. A profile opened meanwhile cancels what is left.</summary>
    private async Task LoadFeedAsync(bool reset)
    {
        if (_viewing == Guid.Empty) return;
        if (!reset && (_feedLoading || !_feedHasMore)) return;
        var token = reset ? ++_feedFor : _feedFor; var who = _viewing; _feedLoading = true;
        if (reset) { Clear(_feedRows); _feedCursor = null; _feedHasMore = false; _feedMore.Visible = false; }
        SetFeedState("Carregando…");
        var reply = await _api.Feed(who, reset ? null : _feedCursor);
        if (token != _feedFor) return;
        _feedLoading = false;
        if (!reply.Ok) { SetFeedState($"Não foi possível carregar o feed. {Describe(reply)}", true); return; }
        RenderFeed(reply.Body, append: !reset);
    }

    private void RenderFeed(JsonElement page, bool append)
    {
        if (!append) { Clear(_feedRows); _feedCursor = null; }
        var hidden = Str(page, "hidden");
        if (hidden.Length > 0) { _feedHasMore = false; _feedMore.Visible = false; SetFeedState(hidden == "private" ? "Este perfil é privado." : "Você não pode ver as publicações desta pessoa."); return; }
        var items = page.ValueKind == JsonValueKind.Object && page.TryGetProperty("items", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToList() : [];
        foreach (var item in items) { _feedRows.AddChild(Str(item, "kind") == "post" ? PostCard(item) : AchievementCard(item)); }
        if (items.Count > 0 && DateTimeOffset.TryParse(Str(items[^1], "at"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var last)) _feedCursor = last;
        _feedHasMore = Flag(page, "more"); _feedMore.Visible = _feedHasMore;
        var empty = _feedRows.GetChildCount() == 0;
        SetFeedState(empty ? (IsSelf ? "Você ainda não publicou nada. Escreva algo acima." : "Nenhuma publicação ainda.") : "");
    }

    private static string When(string iso) => DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Pt) : "";

    private PanelContainer PostCard(JsonElement post)
    {
        Guid.TryParse(Str(post, "postId"), out var postId); var friendsOnly = Str(post, "visibility") == "friends";
        var card = new PanelContainer(); card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.FieldFill, new Color(UiStyle.BorderSoft, .6f), 10, 1, 10, 8));
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 6); card.AddChild(box);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 8); box.AddChild(head);
        head.AddChild(UiStyle.IconRect(friendsOnly ? "lock" : "globe", friendsOnly ? UiStyle.Orange : UiStyle.TextMuted, 18));
        var date = Text(When(Str(post, "at")) + (friendsOnly ? " · só amigos" : ""), 13, false, UiStyle.TextMuted); date.AutowrapMode = TextServer.AutowrapMode.Off; head.AddChild(date);
        if (IsSelf && postId != Guid.Empty)
        {
            var delete = new Button { Name = "DeletePost", Text = "Excluir", FocusMode = Control.FocusModeEnum.None, TooltipText = "Apaga esta publicação", CustomMinimumSize = new Vector2(0, 30) }; UiStyle.StyleButton(delete, UiStyle.ButtonKind.Tool, 12, 3, 8);
            delete.Pressed += () => Confirm("Excluir publicação", "Esta publicação some do seu perfil. Deseja continuar?", "Excluir", () => _ = DeletePostAsync(postId), true); head.AddChild(delete);
        }
        if (Str(post, "text") is { Length: > 0 } text) box.AddChild(Text(text, 16));
        if (Flag(post, "hasImage") && postId != Guid.Empty)
        {
            var picture = new TextureRect { Name = "PostImage", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, CustomMinimumSize = new Vector2(0, 220), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipContents = true };
            box.AddChild(picture); _ = LoadPostImageAsync(picture, postId, _feedFor);
        }
        return card;
    }

    private async Task LoadPostImageAsync(TextureRect target, Guid postId, int token)
    {
        if (await FetchAsync($"/api/v1/users/{_viewing}/profile/posts/{postId}/image") is not { } bytes || token != _feedFor || !GodotObject.IsInstanceValid(target) || TextureOf(bytes) is not { } texture) { if (GodotObject.IsInstanceValid(target) && token == _feedFor) target.CustomMinimumSize = new Vector2(0, 0); return; }
        target.Texture = texture;
    }

    private PanelContainer AchievementCard(JsonElement achievement)
    {
        var card = new PanelContainer(); card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.FieldFill, new Color(UiStyle.Orange, .55f), 10, 1, 10, 8));
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); card.AddChild(row);
        row.AddChild(UiStyle.IconRect(UiIconData.Icons.ContainsKey(Str(achievement, "icon")) ? Str(achievement, "icon") : "star", UiStyle.Orange, 28));
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; text.AddThemeConstantOverride("separation", 0); row.AddChild(text);
        text.AddChild(Text("Conquista: " + Str(achievement, "title"), 16, true)); text.AddChild(Text(Str(achievement, "text"), 13, false, UiStyle.TextMuted));
        var date = Text(When(Str(achievement, "at"))[..Math.Min(10, When(Str(achievement, "at")).Length)], 13, false, UiStyle.Orange); date.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; date.AutowrapMode = TextServer.AutowrapMode.Off; row.AddChild(date);
        return card;
    }

    // ---- writing (mine) ------------------------------------------------------------------------------------------------------

    private async Task PublishAsync()
    {
        var text = _postText.Text.Trim(); var image = _postAttach.ButtonPressed && Current is { } entry ? entry.Id : (Guid?)null;
        if (text.Length == 0 && image is null) { _postNote.Text = "Escreva algo ou anexe uma imagem."; return; }
        _publish.Disabled = true; _postNote.Text = "Publicando…";
        var reply = await _api.Post(text, image, _postVisibility.Selected == 1 ? "friends" : "public");
        _publish.Disabled = false;
        if (!reply.Ok) { _postNote.Text = Describe(reply); return; }
        _postText.Text = ""; _postCount.Text = $"0/{MaxPostText}"; _postAttach.ButtonPressed = false; _postNote.Text = "Publicado.";
        _feedFor++; _feedLoading = false; RenderFeed(reply.Body, append: false);
    }

    private async Task DeletePostAsync(Guid post)
    {
        _status.Text = "Excluindo…"; var reply = await _api.DeletePost(post);
        if (!reply.Ok) { _status.Text = Describe(reply); return; }
        _feedFor++; _feedLoading = false; RenderFeed(reply.Body, append: false); _status.Text = "Publicação excluída.";
    }
}
