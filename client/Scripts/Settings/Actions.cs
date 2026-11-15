using Godot;

namespace LifeVerse.Client.Scripts.Settings;

/// <summary>What the buttons of the window do (Configurações › ações). Each handler is registered by the system that owns the action; the window only runs it by id.</summary>
public static class SettingsActions
{
    private static readonly Dictionary<string, Action<SettingsWindow>> Handlers = [];
    public static void Register(string id, Action<SettingsWindow> handler) => Handlers[id] = handler;
    public static bool Has(string id) => Handlers.ContainsKey(id);

    public static void Run(string id, SettingsWindow window)
    {
        if (!Handlers.TryGetValue(id, out var handler)) throw new InvalidOperationException("Esta ação não está disponível agora (ela depende de a ilha estar aberta).");
        handler(window);
    }
}
