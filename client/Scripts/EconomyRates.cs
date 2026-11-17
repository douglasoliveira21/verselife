using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// <summary>The fees of the platform as the server reports them (basis points, 500 = 5 %), asked for once and kept, so a purchase can show its fee before the person confirms. Until the answer comes the usual 5 % is shown; the server is the one that charges.</summary>
public static class EconomyRates
{
    public const long DefaultBps = 500;
    public static long MarketplaceBps { get; private set; } = DefaultBps;
    public static long ObjectSaleBps { get; private set; } = DefaultBps;
    private static bool _asked;

    public static void Set(long marketplaceBps, long objectSaleBps) { MarketplaceBps = marketplaceBps; ObjectSaleBps = objectSaleBps; _asked = true; }

    /// <summary>"Taxa da plataforma: 5% (VC$ 75), descontada de quem vende."</summary>
    public static string FeeText(long price, long bps)
    {
        var fee = price * bps / 10000; var percent = (bps / 100.0).ToString(bps % 100 == 0 ? "0" : "0.##", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        return fee > 0 ? $"Taxa da plataforma: {percent}% (VC$ {fee.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))}), descontada de quem vende." : $"Taxa da plataforma: {percent}%, descontada de quem vende.";
    }

    /// <summary>Asks the server for the rates the first time (the node only gives access to the tree and the session).</summary>
    public static void EnsureLoaded(Node anchor)
    {
        if (_asked || !GodotObject.IsInstanceValid(anchor)) return;
        var session = anchor.GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = anchor.GetNodeOrNull<AppManager>("/root/AppManager");
        if (session is not { IsAuthenticated: true } || app is null) return;
        _asked = true; var http = new HttpRequest { Timeout = 10 }; anchor.AddChild(http);
        http.RequestCompleted += (result, code, _, body) =>
        {
            http.QueueFree();
            if (result != (long)HttpRequest.Result.Success || code is < 200 or >= 300) { _asked = false; return; }
            try { using var d = JsonDocument.Parse(Encoding.UTF8.GetString(body)); MarketplaceBps = d.RootElement.GetProperty("marketplaceBps").GetInt64(); ObjectSaleBps = d.RootElement.GetProperty("objectSaleBps").GetInt64(); }
            catch (Exception e) when (e is JsonException or KeyNotFoundException) { _asked = false; }
        };
        if (http.Request($"{app.ApiBaseUrl}/api/v1/economy/fees", [$"Authorization: Bearer {session.AccessToken}"]) != Error.Ok) { http.QueueFree(); _asked = false; }
    }
}
