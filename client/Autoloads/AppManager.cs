using Godot;

namespace LifeVerse.Client.Autoloads;

public partial class AppManager : Node
{
    public const string Version = "0.1.0";
    public string ApiBaseUrl { get; private set; } = "http://localhost:8080";

    public override void _Ready()
    {
        var configured = OS.GetEnvironment("LIFEVERSE_API_URL");
        if (!string.IsNullOrWhiteSpace(configured)) ApiBaseUrl = configured.TrimEnd('/');
        GD.Print($"event=client_started version={Version}");
    }
}
