using System.Text;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Updates;

namespace LifeVerse.Client.Scripts;

/// Asks the API once at start whether a newer client exists. Never blocks anything by itself: the login screen and the
/// viewer read `Status` and decide what to show. No answer (offline, old API) simply means "unknown".
public partial class UpdateChecker : Node
{
    [Signal] public delegate void CheckedEventHandler();
    public UpdateStatus Status { get; private set; } = UpdateStatus.Unknown;
    public ReleaseInfo? Release { get; private set; }

    public static string PlatformName => OS.GetName().ToLowerInvariant() switch { "windows" => "windows", "linux" => "linux", var other => other };

    public void Start(string apiBaseUrl)
    {
        var http = new HttpRequest { Timeout = 8 };
        AddChild(http);
        http.RequestCompleted += (_, code, _, body) => { Apply(code, Encoding.UTF8.GetString(body)); http.QueueFree(); };
        if (http.Request($"{apiBaseUrl}/api/v1/client/release?platform={PlatformName}") != Error.Ok) http.QueueFree();
    }

    public void Apply(long code, string body)
    {
        Release = code == 200 ? ReleaseInfo.TryParse(body) : null;
        Status = Release is null ? UpdateStatus.Unknown : UpdateRules.Evaluate(AppManager.Version, Release.Latest, Release.Minimum);
        GD.Print($"event=update_check status={Status} latest={Release?.Latest} minimum={Release?.Minimum}");
        EmitSignal(SignalName.Checked);
    }

    public void OpenDownload() { if (Release is { HasSafeDownloadLink: true }) OS.ShellOpen(Release.DownloadUrl); }

    public string Describe() => Status switch
    {
        UpdateStatus.Required => $"Atualização obrigatória: instale a versão {Release?.Latest} para continuar.",
        UpdateStatus.Available => $"Nova versão disponível: {Release?.Latest} (você tem {AppManager.Version}).",
        _ => string.Empty
    };
}
