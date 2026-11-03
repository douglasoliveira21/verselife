using Godot;

namespace LifeVerse.Client.Scripts;

/// Small anti-aliased shapes drawn into images for the controls Godot has no ready-made look for: check boxes, radio
/// buttons and the on/off switch. Everything is rendered at twice the display size and shown smaller, so edges stay clean.
public static class UiDraw
{
    private const float Scale = 2f;
    private static readonly Dictionary<string, Texture2D> Cache = [];

    // signed distance helpers (negative inside) --------------------------------------------------------------------------
    private static float RoundedRect(float px, float py, float cx, float cy, float halfW, float halfH, float radius)
    {
        var qx = MathF.Abs(px - cx) - (halfW - radius); var qy = MathF.Abs(py - cy) - (halfH - radius);
        return MathF.Sqrt(MathF.Max(qx, 0) * MathF.Max(qx, 0) + MathF.Max(qy, 0) * MathF.Max(qy, 0)) + MathF.Min(MathF.Max(qx, qy), 0) - radius;
    }

    private static float Segment(float px, float py, float ax, float ay, float bx, float by)
    {
        var vx = bx - ax; var vy = by - ay; var wx = px - ax; var wy = py - ay;
        var t = Math.Clamp((wx * vx + wy * vy) / (vx * vx + vy * vy), 0f, 1f);
        var dx = wx - vx * t; var dy = wy - vy * t;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static Image Blank(int width, int height)
    {
        var image = Image.CreateEmpty((int)(width * Scale), (int)(height * Scale), false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0));
        return image;
    }

    private static void Paint(Image image, Func<float, float, float> distance, Color color, float edge = 1f)
    {
        for (var y = 0; y < image.GetHeight(); y++)
        for (var x = 0; x < image.GetWidth(); x++)
        {
            var coverage = Math.Clamp(.5f - distance(x + .5f, y + .5f) / edge, 0f, 1f);
            if (coverage <= 0f) continue;
            var below = image.GetPixel(x, y);
            var alpha = coverage * color.A;
            var outAlpha = alpha + below.A * (1f - alpha);
            var rgb = outAlpha <= 0f ? Colors.Black : new Color((color.R * alpha + below.R * below.A * (1f - alpha)) / outAlpha, (color.G * alpha + below.G * below.A * (1f - alpha)) / outAlpha, (color.B * alpha + below.B * below.A * (1f - alpha)) / outAlpha);
            image.SetPixel(x, y, new Color(rgb.R, rgb.G, rgb.B, outAlpha));
        }
    }

    private static Texture2D Make(string key, int width, int height, Action<Image, float> draw)
    {
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var image = Blank(width, height); draw(image, Scale);
        var texture = ImageTexture.CreateFromImage(image);
        Cache[key] = texture;
        return texture;
    }

    // check box ---------------------------------------------------------------------------------------------------------
    public static Texture2D CheckBox(bool on, bool hover = false, bool disabled = false) => Make($"cb{on}{hover}{disabled}", 20, 20, (image, s) =>
    {
        var border = disabled ? new Color(UiStyle.Border, .45f) : on ? UiStyle.Accent : hover ? UiStyle.BorderBright : UiStyle.Border;
        var fill = on ? UiStyle.AccentFill : new Color(UiStyle.FieldFill, .6f);
        Paint(image, (x, y) => RoundedRect(x, y, 10 * s, 10 * s, 9 * s, 9 * s, 4 * s), border);
        Paint(image, (x, y) => RoundedRect(x, y, 10 * s, 10 * s, 7.4f * s, 7.4f * s, 3 * s), fill);
        if (!on) return;
        var tick = disabled ? UiStyle.TextDisabled : UiStyle.Accent;
        Paint(image, (x, y) => MathF.Min(Segment(x, y, 5.5f * s, 10.5f * s, 8.6f * s, 13.6f * s), Segment(x, y, 8.6f * s, 13.6f * s, 14.6f * s, 6.6f * s)) - 1.1f * s, tick);
    });

    // radio button ------------------------------------------------------------------------------------------------------
    public static Texture2D Radio(bool on, bool hover = false, bool disabled = false) => Make($"rb{on}{hover}{disabled}", 20, 20, (image, s) =>
    {
        var border = disabled ? new Color(UiStyle.Border, .45f) : on ? UiStyle.Accent : hover ? UiStyle.BorderBright : UiStyle.Border;
        Paint(image, (x, y) => MathF.Sqrt((x - 10 * s) * (x - 10 * s) + (y - 10 * s) * (y - 10 * s)) - 9 * s, border);
        Paint(image, (x, y) => MathF.Sqrt((x - 10 * s) * (x - 10 * s) + (y - 10 * s) * (y - 10 * s)) - 7.4f * s, new Color(UiStyle.FieldFill, .6f));
        if (on) Paint(image, (x, y) => MathF.Sqrt((x - 10 * s) * (x - 10 * s) + (y - 10 * s) * (y - 10 * s)) - 4.4f * s, disabled ? UiStyle.TextDisabled : UiStyle.Accent);
    });

    // switch ------------------------------------------------------------------------------------------------------------
    public static Texture2D Switch(bool on, bool hover = false, bool disabled = false) => Make($"sw{on}{hover}{disabled}", 44, 24, (image, s) =>
    {
        var track = disabled ? new Color("#3A4147") : on ? new Color("#22D3C0") : new Color("#3E474E");
        var edge = disabled ? new Color(UiStyle.Border, .4f) : on ? UiStyle.Accent : hover ? UiStyle.BorderBright : UiStyle.Border;
        Paint(image, (x, y) => RoundedRect(x, y, 22 * s, 12 * s, 21 * s, 11 * s, 11 * s), edge);
        Paint(image, (x, y) => RoundedRect(x, y, 22 * s, 12 * s, 19.6f * s, 9.6f * s, 9.6f * s), track);
        var knobX = (on ? 32f : 12f) * s;
        Paint(image, (x, y) => MathF.Sqrt((x - knobX) * (x - knobX) + (y - 12 * s) * (y - 12 * s)) - 8 * s, disabled ? new Color("#8A9299") : Colors.White);
    });
}
