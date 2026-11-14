using Godot;

namespace LifeVerse.Client.Scripts.Interface;

/// <summary>
/// The scale of the interface: <b>Automático</b> follows the size of the window (readable on a 720p laptop, proportional on a 4K monitor); the manual choices are fixed factors.
/// The factor is applied to the window's content scale, so every control, font and icon grows together and stays sharp.
/// </summary>
public static class InterfaceScale
{
    public const float Min = .6f, Max = 2f, AutoMin = .75f, AutoMax = 1.5f, ReferenceWidth = 1920f, ReferenceHeight = 1080f, ReferenceFactor = .8f, ReadableScale = .72f;
    /// <summary>The manual choices, in the order of the menu (after "Automático").</summary>
    public static readonly float[] Choices = [.75f, .85f, .9f, 1f, 1.1f, 1.25f, 1.5f];

    public static string Label(float factor) => $"{Mathf.RoundToInt(factor * 100)}%";
    public static string[] MenuLabels() => ["Automático", .. Choices.Select(Label)];

    /// <summary>
    /// The game stretches the interface from a 1920×1080 design to the window (stretch = the smaller of width/1920 and height/1080) and the factor multiplies that.
    /// "Automático" gives 80% (the usual size) wherever the stretch already makes the interface large enough, and raises the factor on small windows so the effective scale never falls below 72% (text stays readable on a 1280×720 laptop). Always within 75%–150%.
    /// </summary>
    public static float Auto(float windowWidth, float windowHeight)
    {
        var stretch = Math.Max(.1f, Math.Min(Math.Max(1f, windowWidth) / ReferenceWidth, Math.Max(1f, windowHeight) / ReferenceHeight));
        return Mathf.Clamp(Math.Max(ReferenceFactor, ReadableScale / stretch), AutoMin, AutoMax);
    }

    /// <summary>What the person actually sees: window pixels per interface unit (stretch × factor).</summary>
    public static float Effective(float windowWidth, float windowHeight, float factor) => Math.Min(windowWidth / ReferenceWidth, windowHeight / ReferenceHeight) * factor;

    public static float Resolve(bool automatic, float manual, float windowWidth, float windowHeight) => Mathf.Clamp(automatic ? Auto(windowWidth, windowHeight) : manual, Min, Max);

    /// <summary>Index of the menu entry for these settings (0 = Automático).</summary>
    public static int MenuIndex(bool automatic, float manual)
    {
        if (automatic) return 0;
        var best = 0; for (var i = 1; i < Choices.Length; i++) if (Math.Abs(Choices[i] - manual) < Math.Abs(Choices[best] - manual)) best = i;
        return best + 1;
    }

    /// <summary>Applies the factor to the window and returns it.</summary>
    public static float Apply(Window window, bool automatic, float manual)
    {
        // Size.Y is in real pixels, which is the height "Automático" cares about
        var factor = Resolve(automatic, manual, window.Size.X, window.Size.Y);
        window.ContentScaleFactor = factor;
        return factor;
    }
}
