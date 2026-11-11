using Godot;

namespace LifeVerse.Client.Build;

/// <summary>Little pictures of the primitives for the creation grid, drawn as vector shapes so they stay sharp at any size and need no image files.</summary>
public static class BuildIcons
{
    private static readonly Dictionary<string, Texture2D> Cache = [];

    private const string Stroke = "stroke='#E8F7F5' stroke-width='1.6' stroke-linejoin='round' stroke-linecap='round'";
    private const string Fill = "fill='#12F2E0' fill-opacity='.14'";

    private static string Body(string type) => type switch
    {
        "cube" => $"<path d='M4 8 12 4l8 4v9l-8 4-8-4z' {Fill} {Stroke}/><path d='M4 8l8 4 8-4M12 12v9' fill='none' {Stroke}/>",
        "sphere" => $"<circle cx='12' cy='12' r='8.5' {Fill} {Stroke}/><ellipse cx='12' cy='12' rx='8.5' ry='3' fill='none' {Stroke}/><path d='M12 3.5v17' fill='none' {Stroke}/>",
        "cylinder" => $"<path d='M5 6v12c0 1.7 3.1 3 7 3s7-1.3 7-3V6' {Fill} {Stroke}/><ellipse cx='12' cy='6' rx='7' ry='3' {Fill} {Stroke}/>",
        "prism" => $"<path d='M3 18 12 4l9 14z' {Fill} {Stroke}/><path d='M12 4v14' fill='none' {Stroke}/>",
        "torus" => $"<ellipse cx='12' cy='12' rx='9' ry='6.5' {Fill} {Stroke}/><ellipse cx='12' cy='12' rx='3.5' ry='2' fill='none' {Stroke}/>",
        "tube" => $"<path d='M5 7v10c0 1.7 3.1 3 7 3s7-1.3 7-3V7' {Fill} {Stroke}/><ellipse cx='12' cy='7' rx='7' ry='3' {Fill} {Stroke}/><ellipse cx='12' cy='7' rx='3.6' ry='1.5' fill='none' {Stroke}/>",
        "ring" => $"<ellipse cx='12' cy='13' rx='9' ry='5' {Fill} {Stroke}/><ellipse cx='12' cy='12' rx='6.5' ry='3' fill='none' {Stroke}/>",
        "cone" => $"<path d='M12 3 4.5 17c0 1.7 3.4 3 7.5 3s7.5-1.3 7.5-3z' {Fill} {Stroke}/><ellipse cx='12' cy='17' rx='7.5' ry='3' fill='none' {Stroke}/>",
        "pyramid" => $"<path d='M12 3 3.5 17l8.5 4 8.5-4z' {Fill} {Stroke}/><path d='M12 3v18M3.5 17 20.5 17' fill='none' {Stroke}/>",
        "hemisphere" => $"<path d='M3.5 15a8.5 8.5 0 0 1 17 0z' {Fill} {Stroke}/><ellipse cx='12' cy='15' rx='8.5' ry='3' fill='none' {Stroke}/>",
        "tree" => $"<path d='M12 3 5.5 12h3.5L6 17h12l-3-5h3.5z' {Fill} {Stroke}/><path d='M12 17v4' fill='none' {Stroke}/>",
        "grass" => $"<path d='M4 21c1-6 1.5-9 2.5-13 .5 4 1.5 7 2.2 13M9 21c.4-7 2-12 3-17 1.3 5 2.4 10 3 17M14 21c.7-4 1.7-6.500 3.2-9 .3 3.500.8 6.500 1.200 9' fill='none' {Stroke}/>",
        _ => $"<rect x='4' y='4' width='16' height='16' rx='3' {Fill} {Stroke}/>"
    };

    /// <summary>The picture of a primitive at <paramref name="size"/> pixels square.</summary>
    public static Texture2D Primitive(string type, int size = 56)
    {
        var key = type + "@" + size;
        if (Cache.TryGetValue(key, out var known)) return known;
        var svg = $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' width='24' height='24'>{Body(type)}</svg>";
        var image = new Image();
        var texture = image.LoadSvgFromString(svg, size / 24f) == Error.Ok ? ImageTexture.CreateFromImage(image) : null;
        if (texture is null) { var blank = Image.CreateEmpty(size, size, false, Image.Format.Rgba8); blank.Fill(new Color("#2A3239")); texture = ImageTexture.CreateFromImage(blank); }
        return Cache[key] = texture;
    }
}
