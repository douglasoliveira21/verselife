using Godot;
using LifeVerse.Shared.Land;

namespace LifeVerse.Client.Scripts;

/// <summary>What a download of a sound came to: the file, or the reason (in Portuguese, ready to show) it did not work.</summary>
public readonly record struct MediaResult(byte[]? Bytes, string? Problem);

/// <summary>
/// Downloads a sound that the owner of an island, a parcel or a script pointed to. The address was checked before (https, no login, no machine inside a network), and every
/// redirect is checked again before it is followed, so a link that goes through a download service works but can never lead into a network. Nothing is saved; the bytes go to the caller.
/// </summary>
public static class MediaDownload
{
    public const int MaxRedirects = 4;

    public static void Start(Node host, string url, int maxBytes, Action<MediaResult> done, int hop = 0)
    {
        var http = new HttpRequest { Timeout = 25, MaxRedirects = 0, BodySizeLimit = maxBytes, UseThreads = true };
        host.AddChild(http);
        http.RequestCompleted += (result, code, headers, body) =>
        {
            http.QueueFree();
            if (result != (long)HttpRequest.Result.Success) { done(new MediaResult(null, Why((HttpRequest.Result)result, maxBytes))); return; }
            if (code is 301 or 302 or 303 or 307 or 308)
            {
                var next = Location(url, headers);
                if (next is null) { done(new MediaResult(null, "O servidor mandou o arquivo para outro endereço, mas não disse qual.")); return; }
                if (hop >= MaxRedirects) { done(new MediaResult(null, "O endereço redireciona vezes demais.")); return; }
                if (LandPresence.RefuseRedirect(next) is not null) { done(new MediaResult(null, "O endereço redireciona para um lugar que não é permitido (só https público).")); return; }
                Start(host, next, maxBytes, done, hop + 1);
                return;
            }
            if (code != 200) { done(new MediaResult(null, ReasonFor(code))); return; }
            if (body.Length == 0) { done(new MediaResult(null, "O arquivo veio vazio.")); return; }
            done(new MediaResult(body, null));
        };
        if (http.Request(url) != Error.Ok) { http.QueueFree(); done(new MediaResult(null, "Não foi possível iniciar o download.")); }
    }

    private static string ReasonFor(long code) => code switch
    {
        401 or 403 => $"O servidor negou o acesso ao arquivo (HTTP {code}). O link precisa ser público.",
        404 or 410 => $"O arquivo não foi encontrado (HTTP {code}).",
        429 => "O servidor recusou por excesso de pedidos (HTTP 429). Tente de novo daqui a pouco.",
        >= 500 => $"O servidor do arquivo está com problema (HTTP {code}).",
        _ => $"O servidor respondeu HTTP {code}."
    };

    private static string Why(HttpRequest.Result result, int maxBytes) => result switch
    {
        HttpRequest.Result.BodySizeLimitExceeded => $"O arquivo é grande demais (o limite é {maxBytes / (1024 * 1024)} MB).",
        HttpRequest.Result.CantResolve => "Não foi possível encontrar o servidor desse endereço.",
        HttpRequest.Result.CantConnect or HttpRequest.Result.ConnectionError => "Não foi possível conectar ao servidor desse endereço.",
        HttpRequest.Result.TlsHandshakeError => "A conexão segura (https) com o servidor falhou.",
        HttpRequest.Result.Timeout or HttpRequest.Result.NoResponse => "O servidor demorou demais para responder.",
        _ => "O download falhou."
    };

    private static string? Location(string current, string[] headers)
    {
        foreach (var header in headers)
        {
            if (!header.StartsWith("location:", StringComparison.OrdinalIgnoreCase)) continue;
            var target = header["location:".Length..].Trim();
            return Uri.TryCreate(new Uri(current), target, out var next) ? next.AbsoluteUri : null;
        }
        return null;
    }
}

/// <summary>Tells what a downloaded file is by what is inside it (not by the name of the address): a stream the game can play, or a web page or text that came instead.</summary>
public static class AudioSniff
{
    /// <summary>"ogg" or "mp3" for a sound, null for a web page or text (an address that opens a page is not a sound). An unknown beginning is tried as MP3, which is what most files are.</summary>
    public static string? Kind(byte[] bytes, string url)
    {
        if (bytes.Length >= 4 && bytes[0] == 'O' && bytes[1] == 'g' && bytes[2] == 'g' && bytes[3] == 'S') return "ogg";
        var first = 0;
        while (first < bytes.Length && bytes[first] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') first++;
        if (first < bytes.Length && bytes[first] is (byte)'<' or (byte)'{') return null;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.AbsolutePath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ? "ogg" : "mp3";
    }

    public const string NotASound = "O endereço abriu uma página da web (ou um texto), não um arquivo de áudio. Use o link direto do arquivo .mp3 ou .ogg.";
}
