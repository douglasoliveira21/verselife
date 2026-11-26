using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// The "texturas do sistema", as the Godot client draws them: a few patterns made in code (so they need no upload, no storage and no permission, and everybody sees the same), by the names the server accepts in a
    /// material's "builtin" (and a projector's "projectorBuiltin"). The pixels follow the same formulas as the Godot client.
    /// </summary>
    public static class BuiltInTextures
    {
        public static readonly (string Id, string Label)[] All =
        {
            ("plywood", "Compensado (padrão)"), ("checker", "Xadrez"), ("bricks", "Tijolos"), ("planks", "Tábuas"), ("stripes", "Listras"), ("noise", "Ruído"), ("tiles", "Ladrilhos"),
        };

        public const int Size = 256;
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        public static bool Has(string id) => id != null && All.Any(t => t.Id == id);

        public static Texture2D Get(string id)
        {
            if (!Has(id)) return null; if (Cache.TryGetValue(id, out var cached) && cached != null) return cached;
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true, false) { name = "builtin-" + id, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var pixels = new Color32[Size * Size];
            for (var y = 0; y < Size; y++) for (var x = 0; x < Size; x++) pixels[y * Size + x] = Pixel(id, x, y);
            texture.SetPixels32(pixels); texture.Apply(true, false); return Cache[id] = texture;
        }

        private static Color Shade(float v) => new Color(v, v, v, 1f);

        /// <summary>The colour of one pixel of a pattern (public for the tests: the same numbers as the Godot client).</summary>
        public static Color Pixel(string id, int x, int y)
        {
            switch (id)
            {
                case "plywood":
                {
                    var grain = .5f + .09f * Mathf.Sin(y * .11f + Mathf.Sin(x * .035f) * 2.4f) + .05f * Mathf.Sin(y * .43f + x * .02f) + Hash(x / 3, y / 24) * .06f;
                    return new Color(.74f * grain + .30f, .56f * grain + .22f, .34f * grain + .12f, 1f);
                }
                case "checker": return ((x / 32 + y / 32) % 2 == 0) ? Shade(.92f) : Shade(.25f);
                case "stripes": return (x / 32) % 2 == 0 ? Shade(.9f) : Shade(.35f);
                case "bricks":
                {
                    const int rowHeight = 32, brickWidth = 64; var row = y / rowHeight; var shifted = (x + (row % 2 == 0 ? 0 : brickWidth / 2)) % brickWidth;
                    var mortar = y % rowHeight < 3 || shifted < 3; return mortar ? Shade(.78f) : new Color(.62f + Hash(row, (x + (row % 2 == 0 ? 0 : 32)) / brickWidth) * .2f, .28f, .2f, 1f);
                }
                case "planks": { var plank = x / 64; var grain = .5f + .08f * Mathf.Sin((y + plank * 40) * .09f) + Hash(plank, 0) * .18f; return x % 64 < 2 ? Shade(.18f) : new Color(.55f * grain + .2f, .38f * grain + .12f, .2f * grain + .06f, 1f); }
                case "tiles": { var edge = x % 64 < 2 || y % 64 < 2; return edge ? Shade(.45f) : Shade(.82f + Hash(x / 64, y / 64) * .12f); }
                default: return Shade(.35f + Hash(x / 2, y / 2) * .55f);
            }
        }

        /// <summary>A stable pseudo-random number from 0 to 1 for a pair of cell coordinates (the same as the Godot client's).</summary>
        public static float Hash(int a, int b)
        {
            unchecked { var h = (uint)(a * 374761393 + b * 668265263); h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xFFFF) / 65535f; }
        }
    }
}
