#if NETSTANDARD2_1
using System.Net.Http;

namespace LifeVerse.IslandServer.Scripts;

/// <summary>Sobrecargas do .NET 5+ que o netstandard2.1 (Unity) não tem e o código de Scripting/ usa.</summary>
internal static class HttpCompat
{
    public static Task<string> ReadAsStringAsync(this HttpContent content, CancellationToken ct) => content.ReadAsStringAsync();
}
#endif
