using Godot;

namespace LifeVerse.Client.Scripts;

/// The look of the interface in one place: colours measured from the design references (client/Assets/ui), the Inter font,
/// the SVG icon set and the panel/button styles built from them. Everything else asks this class instead of hard-coding looks.
public static class UiStyle
{
    // ---- colours (measured on the reference images) -------------------------------------------------------------------
    public static readonly Color PanelFill = new(.106f, .149f, .180f, .84f);       // glass panel, #1B262E
    public static readonly Color PanelFillSolid = new("#1B262E");
    public static readonly Color TitleFill = new(.118f, .165f, .216f, .90f);       // window title bar, #1E2A37
    public static readonly Color RowFill = new(.137f, .176f, .212f, .70f);         // list rows / cards, #232D36
    public static readonly Color BorderSoft = new("#6D818D");                      // window outline
    public static readonly Color Border = new("#91979B");                          // buttons and bars
    public static readonly Color BorderBright = new("#A9BCC3");
    public static readonly Color Divider = new("#4E5D67");
    public static readonly Color Text = new("#FFFFFF");
    public static readonly Color TextMuted = new("#A9B3BD");
    public static readonly Color TextDisabled = new("#7B858C");

    public static readonly Color Accent = new("#12F2E0");                          // neon outline / underline / progress
    public static readonly Color AccentText = new("#2EE6D0");
    public static readonly Color AccentFill = new("#0A3A3C");                      // selected teal button fill
    public static readonly Color AccentFillHover = new("#12706F");
    public static readonly Color AccentFillPressed = new("#07292B");
    public static readonly Color AccentFillStrong = new("#1B8F88");

    public static readonly Color Orange = new("#F4A340");
    public static readonly Color OrangeFill = new("#4B3D30");
    public static readonly Color OrangeFillHover = new("#6B4E2E");
    public static readonly Color OrangeFillPressed = new("#5A3F22");

    public static readonly Color ButtonFill = new("#252D33");
    public static readonly Color ButtonFillHover = new("#34414A");
    public static readonly Color ButtonFillPressed = new("#1A2126");
    public static readonly Color ButtonFillDisabled = new("#2B3035");
    public static readonly Color FieldFill = new("#2A3239");

    public static readonly Color Success = new("#22C55E");
    public static readonly Color SuccessBright = new("#3BF45F");
    public static readonly Color Warning = new("#F59E0B");
    public static readonly Color Danger = new("#EF4444");
    public static readonly Color DangerBright = new("#FF6B6B");
    public static readonly Color Coin = new("#F5B747");

    // ---- sizes -------------------------------------------------------------------------------------------------------
    public const int RadiusSmall = 6;
    public const int Radius = 10;
    public const int RadiusLarge = 12;
    public const int FontBody = 14;
    public const int FontSmall = 12;
    public const int FontTitle = 18;

    // ---- font --------------------------------------------------------------------------------------------------------
    private static FontFile? _regular, _medium, _bold;
    public static Font Regular => _regular ??= LoadFont("Inter-Regular.ttf");
    public static Font Medium => _medium ??= LoadFont("Inter-Medium.ttf");
    public static Font Bold => _bold ??= LoadFont("Inter-Bold.ttf");

    private static FontFile LoadFont(string file)
    {
        var path = $"res://Assets/fonts/{file}";
        var bytes = Godot.FileAccess.FileExists(path) ? Godot.FileAccess.GetFileAsBytes(path) : [];
        // A missing file leaves an empty font, which Godot replaces with its default: the interface still works.
        return new FontFile { Data = bytes, Antialiasing = TextServer.FontAntialiasing.Gray, SubpixelPositioning = TextServer.SubpixelPositioning.Auto, GenerateMipmaps = false };
    }

    // ---- icons -------------------------------------------------------------------------------------------------------
    private static readonly Dictionary<(string, string, int), Texture2D> IconCache = [];

    /// An icon from the embedded set, drawn in `color`, for display at `size` pixels (rendered at twice that for sharpness).
    public static Texture2D Icon(string name, Color? color = null, int size = 20)
    {
        var tint = color ?? Text;
        var key = (name, tint.ToHtml(false), size);
        if (IconCache.TryGetValue(key, out var cached)) return cached;
        if (!UiIconData.Icons.TryGetValue(name, out var data)) data = UiIconData.Icons["dots"];
        var hex = $"#{tint.ToHtml(false)}";
        var svg = data.Filled
            ? $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"{hex}\">{data.Inner}</svg>"
            : $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"{hex}\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">{data.Inner}</svg>";
        var image = new Image();
        var error = image.LoadSvgFromString(svg, size * 2f / 24f);
        var texture = error == Error.Ok ? ImageTexture.CreateFromImage(image) : (Texture2D)new PlaceholderTexture2D { Size = new Vector2(size, size) };
        IconCache[key] = texture;
        return texture;
    }

    /// A TextureRect showing an icon at an exact size.
    public static TextureRect IconRect(string name, Color? color = null, int size = 20) => new()
    {
        Texture = Icon(name, color, size), CustomMinimumSize = new Vector2(size, size), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
    };

    // ---- brand -------------------------------------------------------------------------------------------------------
    /// The LifeVerse mark (lotus) or the full logo (lotus and name), cut out on a transparent background, scaled to this height.
    public static TextureRect Brand(bool fullLogo, int height)
    {
        var texture = GD.Load<Texture2D>(fullLogo ? "res://Assets/brand/logo.png" : "res://Assets/brand/icon.png");
        var ratio = texture is null ? 1f : texture.GetWidth() / (float)texture.GetHeight();
        return new TextureRect
        {
            Texture = texture, CustomMinimumSize = new Vector2(height * ratio, height), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps
        };
    }

    // ---- boxes -------------------------------------------------------------------------------------------------------
    public static StyleBoxFlat Box(Color fill, Color border, int radius = Radius, int borderWidth = 1, float marginX = 12, float marginY = 7, int shadow = 0)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = border, AntiAliasing = true, AntiAliasingSize = 1f };
        box.SetBorderWidthAll(borderWidth); box.SetCornerRadiusAll(radius);
        box.ContentMarginLeft = box.ContentMarginRight = marginX; box.ContentMarginTop = box.ContentMarginBottom = marginY;
        if (shadow > 0) { box.ShadowColor = new Color(0, 0, 0, .38f); box.ShadowSize = shadow; box.ShadowOffset = new Vector2(0, 3); }
        return box;
    }

    /// The glass look of windows and bars: dark translucent fill, thin light outline, soft shadow.
    public static StyleBoxFlat Glass(float alpha = .84f, int radius = RadiusLarge, float margin = 0, bool outlineSoft = true) =>
        Box(new Color(PanelFill, alpha), outlineSoft ? BorderSoft : Border, radius, 1, margin, margin, 14);

    public enum ButtonKind { Tool, ToolSelected, Primary, Secondary, Field }
    public enum ButtonState { Normal, Hover, Pressed, Disabled }

    public static StyleBoxFlat ButtonBox(ButtonKind kind, ButtonState state, float marginX = 12, float marginY = 7, int radius = Radius)
    {
        var (fill, border, width) = (kind, state) switch
        {
            (ButtonKind.Tool, ButtonState.Normal) => (ButtonFill, Border, 1),
            (ButtonKind.Tool, ButtonState.Hover) => (ButtonFillHover, BorderBright, 1),
            (ButtonKind.Tool, ButtonState.Pressed) => (ButtonFillPressed, BorderBright, 1),
            (ButtonKind.ToolSelected, ButtonState.Normal) => (AccentFill, Accent, 2),
            (ButtonKind.ToolSelected, ButtonState.Hover) => (AccentFillHover, Accent, 2),
            (ButtonKind.ToolSelected, ButtonState.Pressed) => (AccentFillPressed, Accent, 2),
            (ButtonKind.Primary, ButtonState.Normal) => (AccentFill, Accent, 2),
            (ButtonKind.Primary, ButtonState.Hover) => (AccentFillStrong, Accent, 2),
            (ButtonKind.Primary, ButtonState.Pressed) => (AccentFillPressed, Accent, 2),
            (ButtonKind.Secondary, ButtonState.Normal) => (OrangeFill, Orange, 2),
            (ButtonKind.Secondary, ButtonState.Hover) => (OrangeFillHover, Orange, 2),
            (ButtonKind.Secondary, ButtonState.Pressed) => (OrangeFillPressed, Orange, 2),
            (ButtonKind.Field, _) => (FieldFill, Border, 1),
            _ => (ButtonFillDisabled, new Color(Border, .45f), 1)
        };
        return Box(fill, border, radius, width, marginX, marginY);
    }

    /// Applies one button kind to a button through theme overrides (all four states).
    public static void StyleButton(Button button, ButtonKind kind, float marginX = 12, float marginY = 7, int radius = Radius)
    {
        button.AddThemeStyleboxOverride("normal", ButtonBox(kind, ButtonState.Normal, marginX, marginY, radius));
        button.AddThemeStyleboxOverride("hover", ButtonBox(kind, ButtonState.Hover, marginX, marginY, radius));
        button.AddThemeStyleboxOverride("pressed", ButtonBox(kind, ButtonState.Pressed, marginX, marginY, radius));
        button.AddThemeStyleboxOverride("hover_pressed", ButtonBox(kind, ButtonState.Pressed, marginX, marginY, radius));
        button.AddThemeStyleboxOverride("disabled", ButtonBox(kind, ButtonState.Disabled, marginX, marginY, radius));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.AddThemeColorOverride("font_disabled_color", TextDisabled);
        button.AddThemeFontOverride("font", Medium);
    }
}
