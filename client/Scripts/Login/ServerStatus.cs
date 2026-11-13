using Godot;

namespace LifeVerse.Client.Scripts.LoginUi;

/// <summary>"Servidores Online / Indisponíveis", from the health check of the API (<c>/health</c>), repeated every 20 seconds. Until the first answer it says it is checking; it never shows a made-up state.</summary>
public partial class ServerStatus : HBoxContainer
{
    public enum State { Checking, Online, Offline }

    private HttpRequest _http = null!;
    private ColorRect _dot = null!;
    private Label _text = null!;
    private double _wait;
    private string _address = "";
    public State Current { get; private set; } = State.Checking;
    public Func<string, string, string?, Task<(long, string)>>? Transport { get; set; }
    public bool UseRequests { get; set; } = true;
    public string TextNow => _text.Text;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 10); MouseFilter = MouseFilterEnum.Ignore;
        _dot = new ColorRect { CustomMinimumSize = new Vector2(12, 12), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore }; AddChild(_dot);
        _text = new Label(); _text.AddThemeFontOverride("font", UiStyle.Medium); _text.AddThemeFontSizeOverride("font_size", 15); AddChild(_text);
        _http = new HttpRequest { Timeout = 6 }; AddChild(_http); _http.RequestCompleted += OnCompleted;
        Show(State.Checking);
    }

    public void Start(string apiAddress) { _address = apiAddress.TrimEnd('/'); _wait = 0; }

    public override void _Process(double delta)
    {
        if (_address.Length == 0 || !UseRequests) return;
        _wait -= delta; if (_wait > 0) return; _wait = 20;
        if (_http.GetHttpClientStatus() == Godot.HttpClient.Status.Disconnected && _http.Request(_address + "/health") != Error.Ok) Show(State.Offline);
    }

    private void OnCompleted(long result, long code, string[] headers, byte[] body) => Show(result == (long)HttpRequest.Result.Success && code is >= 200 and < 300 ? State.Online : State.Offline);

    public void Show(State state)
    {
        Current = state;
        var (color, text) = state switch { State.Online => (UiStyle.SuccessBright, "Servidores Online"), State.Offline => (UiStyle.Danger, "Servidores Indisponíveis"), _ => (UiStyle.TextMuted, "Verificando servidores…") };
        _dot.Color = color; _text.Text = text; _text.AddThemeColorOverride("font_color", state == State.Checking ? UiStyle.TextMuted : UiStyle.Text);
    }
}
