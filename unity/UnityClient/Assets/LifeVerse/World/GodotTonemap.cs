using System.Collections.Generic;
using UnityEngine;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// The last step of the picture as the Godot client does it: the filmic tonemapper with white at 6 and, when "Ajuste de cor" is on, the contrast and saturation of the quality
    /// preset on the sRGB result. HDRP takes it as an external look-up table: it is asked the colour in the LogC encoding and gives back linear colour.
    /// </summary>
    public static class GodotTonemap
    {
        public const float White = 6f;
        private static readonly Dictionary<(int Size, int Contrast, int Saturation, int Under), Texture3D> Tables = new Dictionary<(int, int, int, int), Texture3D>();

        /// <summary>How much the colour of the water covers the picture seen from under it (the Godot client lays it over the screen).</summary>
        public const float UnderwaterCover = .5f;
        /// <summary>That colour, by day factor: a clear blue at noon, almost black at night (sRGB, as the Godot overlay).</summary>
        public static Color UnderwaterColour(float day) => Color.Lerp(new Color(0f, .02f, .06f), new Color(.02f, .26f, .4f), day);

        /// <summary>A mapped colour with the water laid over it (blended on screen, in sRGB).</summary>
        public static Color Submerged(Color linear, float day)
        {
            var s = Color.Lerp(linear.gamma, UnderwaterColour(day), UnderwaterCover); s.a = 1f; return s.linear;
        }

        private static float Curve(float x)
        {
            const float bias = 2f, A = .22f * bias * bias, B = .30f * bias, C = .10f, D = .20f, E = .01f, F = .30f;
            return (x * (A * x + C * B) + D * E) / (x * (A * x + B) + D * F) - E / F;
        }

        /// <summary>The filmic curve of Godot: 0 stays 0 and <see cref="White"/> becomes 1.</summary>
        public static float Filmic(float linear) => Mathf.Clamp01(Curve(Mathf.Max(0f, linear)) / Curve(White));

        /// <summary>One colour through the tonemapper and the adjustment: linear in, linear out.</summary>
        public static Color Map(Color linear, float contrast = 1f, float saturation = 1f)
        {
            var mapped = new Color(Filmic(linear.r), Filmic(linear.g), Filmic(linear.b), 1f);
            if (contrast == 1f && saturation == 1f) return mapped;
            var s = mapped.gamma;
            float Contrast(float v) => Mathf.LerpUnclamped(.5f, v, contrast);
            s = new Color(Contrast(s.r), Contrast(s.g), Contrast(s.b), 1f);
            var grey = (s.r + s.g + s.b) * .33333f;
            s = new Color(Mathf.Clamp01(Mathf.LerpUnclamped(grey, s.r, saturation)), Mathf.Clamp01(Mathf.LerpUnclamped(grey, s.g, saturation)), Mathf.Clamp01(Mathf.LerpUnclamped(grey, s.b, saturation)), 1f);
            return s.linear;
        }

        /// <summary>The LogC encoding (Alexa EI 1000) HDRP stores its tables in, back to linear.</summary>
        public static float FromLogC(float value) => (Mathf.Pow(10f, (value - .386036f) / .244161f) - .047996f) / 5.555556f;

        /// <summary>
        /// The look-up table of a size (the one of the pipeline asset) for a contrast and a saturation; made once. With <paramref name="underwaterDay"/> (0 to 1, in tenths)
        /// it also lays the water over the picture.
        /// </summary>
        public static Texture3D Table(int size, float contrast, float saturation, float underwaterDay = -1f)
        {
            var under = underwaterDay < 0f ? -1 : Mathf.RoundToInt(Mathf.Clamp01(underwaterDay) * 10f);
            var key = (size, Mathf.RoundToInt(contrast * 1000f), Mathf.RoundToInt(saturation * 1000f), under);
            if (Tables.TryGetValue(key, out var known) && known != null) return known;
            var pixels = new Color[size * size * size];
            for (var b = 0; b < size; b++)
                for (var g = 0; g < size; g++)
                    for (var r = 0; r < size; r++)
                    {
                        var mapped = Map(new Color(FromLogC(r / (size - 1f)), FromLogC(g / (size - 1f)), FromLogC(b / (size - 1f))), contrast, saturation);
                        pixels[(b * size + g) * size + r] = under < 0 ? mapped : Submerged(mapped, under / 10f);
                    }
            var table = new Texture3D(size, size, size, TextureFormat.RGBAHalf, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "GodotTonemap" };
            table.SetPixels(pixels); table.Apply(false); Tables[key] = table; return table;
        }
    }
}
