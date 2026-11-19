using System.Text.Json;

namespace LifeVerse.IslandServer.Core;

/// <summary>Logs estruturados, uma linha JSON por evento ({timestamp, event, properties}): o mesmo formato do servidor Godot, que o Supervisor e o Easypanel já leem.</summary>
public sealed class IslandLog(TextWriter? output = null)
{
    private readonly TextWriter _output = output ?? Console.Out;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public void Write(string eventName, object? properties = null)
    {
        var line = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, @event = eventName, properties = properties ?? new { } }, Options);
        lock (_gate) { _output.WriteLine(line); _output.Flush(); }
    }
}
