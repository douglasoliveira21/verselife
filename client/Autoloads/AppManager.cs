using Godot;

namespace LifeVerse.Client.Autoloads;

public partial class AppManager : Node
{
    public const string Version = "0.2.0";
    public string ApiBaseUrl { get; private set; } = "http://localhost:8080";

    public override void _Ready()
    {
        var configured = OS.GetEnvironment("LIFEVERSE_API_URL");
        if (!string.IsNullOrWhiteSpace(configured)) ApiBaseUrl = configured.TrimEnd('/');
        var requestedSize = OS.GetEnvironment("LIFEVERSE_WINDOW_SIZE");
        var parts = requestedSize.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height) && width >= 800 && height >= 600)
            DisplayServer.WindowSetSize(new Vector2I(width, height));
        GD.Print($"event=client_started version={Version}");
    }
}
