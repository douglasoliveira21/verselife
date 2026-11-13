using Godot;

namespace LifeVerse.Client.Scripts.LoginUi;

/// <summary>
/// The panoramic picture behind the login. It lives in its own file (<see cref="ImagePath"/>) and carries no interface, so it can be replaced without touching the screen.
/// The picture covers the whole window keeping its proportions (it is cropped, never stretched); a soft dark veil keeps the form readable on any picture.
/// </summary>
public partial class LoginBackground : Control
{
    public const string ImagePath = "res://Assets/ui/Login/login_background.png";

    public TextureRect Picture { get; private set; } = null!;
    public bool HasPicture => Picture.Texture is not null;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); MouseFilter = MouseFilterEnum.Ignore; ClipContents = true;
        AddChild(Fill(new ColorRect { Color = new Color("#0B1118"), MouseFilter = MouseFilterEnum.Ignore })); // shown if the picture is missing
        Picture = new TextureRect { Texture = ResourceLoader.Exists(ImagePath) ? GD.Load<Texture2D>(ImagePath) : null, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps };
        Picture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(Picture);
        var veil = new TextureRect { Texture = Veil(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore }; veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(veil);
    }

    private static T Fill<T>(T control) where T : Control { control.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); return control; }

    /// <summary>Darker at the top (for the logo) and at the bottom (for the status bar), clear in the middle.</summary>
    private static GradientTexture2D Veil()
    {
        var gradient = new Gradient { Offsets = [0f, .28f, .72f, 1f], Colors = [new Color(.03f, .05f, .09f, .62f), new Color(.03f, .05f, .09f, .22f), new Color(.03f, .05f, .09f, .28f), new Color(.03f, .05f, .09f, .82f)] };
        return new GradientTexture2D { Gradient = gradient, Fill = GradientTexture2D.FillEnum.Linear, FillFrom = new Vector2(0, 0), FillTo = new Vector2(0, 1), Width = 4, Height = 256 };
    }
}
