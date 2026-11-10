using Godot;

namespace LifeVerse.Client.Scripts;

/// The "texturas do sistema": a few patterns drawn in code, so they need no upload, no storage and no permission, and every
/// player sees the same thing. The names are the ones the server accepts in a material's "builtin" setting.
public static class BuiltInTextures
{
    public static readonly (string Id, string Label)[] All =
    [
        ("plywood", "Compensado (padrão)"), ("checker", "Xadrez"), ("bricks", "Tijolos"), ("planks", "Tábuas"), ("stripes", "Listras"), ("noise", "Ruído"), ("tiles", "Ladrilhos")
    ];

    private const int Size = 256;
    private static readonly Dictionary<string, Texture2D> Cache = [];

    public static Texture2D? Get(string id)
    {
        if (Cache.TryGetValue(id, out var cached)) return cached;
        if (!All.Any(x => x.Id == id)) return null;
        var image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
        for (var y = 0; y < Size; y++) for (var x = 0; x < Size; x++) image.SetPixel(x, y, Pixel(id, x, y));
        image.GenerateMipmaps();
        return Cache[id] = ImageTexture.CreateFromImage(image);
    }

    private static Color Shade(float value) => new(value, value, value);

    private static Color Pixel(string id, int x, int y)
    {
        switch (id)
        {
            case "plywood": // the default texture of every new object: pale plywood with a soft grain
            {
                var grain = .5f + .09f * MathF.Sin(y * .11f + MathF.Sin(x * .035f) * 2.4f) + .05f * MathF.Sin(y * .43f + x * .02f) + Hash(x / 3, y / 24) * .06f;
                return new Color(.74f * grain + .30f, .56f * grain + .22f, .34f * grain + .12f);
            }
            case "checker": return ((x / 32 + y / 32) % 2 == 0) ? Shade(.92f) : Shade(.25f);
            case "stripes": return (x / 32) % 2 == 0 ? Shade(.9f) : Shade(.35f);
            case "bricks":
            {
                const int rowHeight = 32, brickWidth = 64;
                var row = y / rowHeight; var shifted = (x + (row % 2 == 0 ? 0 : brickWidth / 2)) % brickWidth;
                var mortar = y % rowHeight < 3 || shifted < 3;
                return mortar ? Shade(.78f) : new Color(.62f + Hash(row, (x + (row % 2 == 0 ? 0 : 32)) / brickWidth) * .2f, .28f, .2f);
            }
            case "planks":
            {
                var plank = x / 64; var grain = .5f + .08f * MathF.Sin((y + plank * 40) * .09f) + Hash(plank, 0) * .18f;
                return x % 64 < 2 ? Shade(.18f) : new Color(.55f * grain + .2f, .38f * grain + .12f, .2f * grain + .06f);
            }
            case "tiles":
            {
                var edge = x % 64 < 2 || y % 64 < 2;
                return edge ? Shade(.45f) : Shade(.82f + Hash(x / 64, y / 64) * .12f);
            }
            default: return Shade(.35f + Hash(x / 2, y / 2) * .55f);
        }
    }

    /// A stable pseudo-random number from 0 to 1 for a pair of cell coordinates.
    private static float Hash(int a, int b)
    {
        unchecked { var h = (uint)(a * 374761393 + b * 668265263); h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xFFFF) / 65535f; }
    }
}
