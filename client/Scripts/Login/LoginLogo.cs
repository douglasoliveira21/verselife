using Godot;

namespace LifeVerse.Client.Scripts.LoginUi;

/// <summary>The mark at the top centre: the LifeVerse logo the game already uses everywhere (<see cref="LogoPath"/>) and the line under it. A resource of its own, independent of the background.</summary>
public partial class LoginLogo : VBoxContainer
{
    public const string LogoPath = "res://Assets/brand/logo.png";
    public const string Subtitle = "Mundo Virtual  •  Social  •  Criativo  •  Sem Limites";
    public const int Height = 230;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 0); Alignment = AlignmentMode.Center; MouseFilter = MouseFilterEnum.Ignore;
        var logo = UiStyle.Brand(true, Height); logo.Name = "Logo"; logo.SizeFlagsHorizontal = SizeFlags.ShrinkCenter; AddChild(logo);
        var line = new Label { Text = Subtitle, HorizontalAlignment = HorizontalAlignment.Center, Name = "Subtitle" }; line.AddThemeFontOverride("font", UiStyle.Medium); line.AddThemeFontSizeOverride("font_size", 17); line.AddThemeColorOverride("font_color", new Color("#D4E2EA"));
        line.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .7f)); line.AddThemeConstantOverride("shadow_offset_y", 2); line.AddThemeConstantOverride("shadow_outline_size", 6); AddChild(line);
    }
}
