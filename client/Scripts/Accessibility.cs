namespace LifeVerse.Client.Scripts;

/// <summary>Options that make the game gentler to use (Configurações › Acessibilidade).</summary>
public static class Accessibility
{
    /// <summary>Notices appear and go away without fading, and the window does not flash the row you jump to. Nothing else in the game moves on its own to begin with.</summary>
    public static bool ReduceMotion { get; set; }
}
