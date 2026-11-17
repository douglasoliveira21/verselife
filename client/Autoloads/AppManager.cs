using Godot;

namespace LifeVerse.Client.Autoloads;

public partial class AppManager : Node
{
    public const string Version = "0.6.0";
    /// <summary>The server of the game. A build made for players (an export, not the editor) talks to the real one; the editor and the tests talk to a server on this machine. LIFEVERSE_API_URL overrides both.</summary>
    public const string ProductionApiUrl = "https://api.nerdplus.com.br", DevelopmentApiUrl = "http://localhost:8080";
    public string ApiBaseUrl { get; private set; } = OS.HasFeature("editor") ? DevelopmentApiUrl : ProductionApiUrl;
    public Scripts.UpdateChecker Updates { get; private set; } = null!;

    public override void _Ready()
    {
        var configured = OS.GetEnvironment("LIFEVERSE_API_URL");
        if (!string.IsNullOrWhiteSpace(configured)) ApiBaseUrl = configured.TrimEnd('/');
        var requestedSize = OS.GetEnvironment("LIFEVERSE_WINDOW_SIZE");
        var parts = requestedSize.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height) && width >= 800 && height >= 600)
            DisplayServer.WindowSetSize(new Vector2I(width, height));
        Callable.From(Scripts.Settings.DisplayOptions.KeepTitleBarVisible).CallDeferred();
        Updates = new Scripts.UpdateChecker { Name = "Updates" }; AddChild(Updates);
        if (DisplayServer.GetName() != "headless" && string.IsNullOrEmpty(OS.GetEnvironment("LIFEVERSE_SKIP_UPDATE_CHECK"))) Updates.Start(ApiBaseUrl);
        GD.Print($"event=client_started version={Version} api={ApiBaseUrl}");
    }
}
