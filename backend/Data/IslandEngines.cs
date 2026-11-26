using Microsoft.AspNetCore.Http;

namespace LifeVerse.Api.Data;

/// <summary>
/// The two island servers that live side by side while the game moves from Godot to Unity. Each island runs on one of them, and only the client of the same engine can enter it: the Godot client
/// speaks protocol 11 and the Unity client speaks LifeVerse Wire 12. A client says which one it is with the <c>X-LifeVerse-Engine</c> header; a client that says nothing is the Godot one (every client
/// released before the header existed). Each island supervisor asks only for the islands of its own engine.
/// </summary>
public static class IslandEngines
{
    public const string Godot = "godot", Unity = "unity", Header = "X-LifeVerse-Engine";

    public static bool IsValid(string? engine) => engine is Godot or Unity;

    /// <summary>The engine a word names ("godot" when it is missing or unknown).</summary>
    public static string Normalize(string? engine) => engine?.Trim().ToLowerInvariant() is Unity ? Unity : Godot;

    /// <summary>The engine of the client making a request.</summary>
    public static string OfClient(HttpContext context) => Normalize(context.Request.Headers[Header].ToString());

    /// <summary>Why a client cannot enter an island of another engine, in the words the clients show.</summary>
    public static string Mismatch(string island) => island == Unity
        ? "Esta ilha já roda no novo LifeVerse (Unity). Baixe a nova versão do jogo para entrar."
        : "Esta ilha ainda roda no LifeVerse clássico. Use o cliente clássico para entrar, ou escolha outra ilha.";
}
