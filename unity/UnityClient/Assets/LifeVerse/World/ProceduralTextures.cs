using System;
using UnityEngine;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// Seamless procedural textures, so the project needs no licensed art to look like ground and water (the Godot client does the same with FastNoiseLite).
    /// Everything is periodic value noise: a texture tiles without a visible seam.
    /// </summary>
    public static class ProceduralTextures
    {
        /// <summary>Periodic value noise in 0..1 at (x, y) of a size x size grid with the given number of cells per side.</summary>
        private static float[] Noise(int size, int cells, int seed)
        {
            var random = new System.Random(seed); var lattice = new float[cells * cells];
            for (var i = 0; i < lattice.Length; i++) lattice[i] = (float)random.NextDouble();
            var data = new float[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var fx = (float)x / size * cells; var fy = (float)y / size * cells;
                    var x0 = (int)fx; var y0 = (int)fy; var tx = Smooth(fx - x0); var ty = Smooth(fy - y0);
                    float L(int ix, int iy) => lattice[(iy % cells) * cells + (ix % cells)];
                    var top = Mathf.Lerp(L(x0, y0), L(x0 + 1, y0), tx); var bottom = Mathf.Lerp(L(x0, y0 + 1), L(x0 + 1, y0 + 1), tx);
                    data[y * size + x] = Mathf.Lerp(top, bottom, ty);
                }
            return data;
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        /// <summary>Fractal noise (several octaves of <see cref="Noise"/>), 0..1.</summary>
        public static float[] Fbm(int size, int baseCells, int octaves, int seed)
        {
            var sum = new float[size * size]; var amplitude = 1f; var total = 0f;
            for (var octave = 0; octave < octaves; octave++)
            {
                var layer = Noise(size, baseCells << octave, seed + octave * 31);
                for (var i = 0; i < sum.Length; i++) sum[i] += layer[i] * amplitude;
                total += amplitude; amplitude *= .5f;
            }
            for (var i = 0; i < sum.Length; i++) sum[i] /= total;
            return sum;
        }

        /// <summary>A tiling grey noise (fractal, stretched to use the whole range), linear colour space: a mask for shaders.</summary>
        public static Texture2D GreyNoise(int size, int baseCells, int octaves, int seed)
        {
            var field = Fbm(size, baseCells, octaves, seed); var pixels = new Color32[size * size];
            for (var i = 0; i < pixels.Length; i++) { var v = (byte)(Mathf.Clamp01((field[i] - .5f) * 2.4f + .5f) * 255f); pixels[i] = new Color32(v, v, v, 255); }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, name = "GreyNoise" };
            texture.SetPixels32(pixels); texture.Apply(true); return texture;
        }

        /// <summary>The ground texture of one layer: noise between a dark and a light colour (coarse blotches over fine grain).</summary>
        public static Color32[] Ground(Color dark, Color light, int seed, int size = 256)
        {
            var coarse = Fbm(size, 3, 4, seed); var fine = Fbm(size, 24, 2, seed + 7); var pixels = new Color32[size * size];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = (Color32)Color.Lerp(dark, light, Mathf.Clamp01(coarse[i] * .65f + fine[i] * .35f));
            return pixels;
        }

        /// <summary>A tangent-space normal map from a height field (the water ripples).</summary>
        public static Texture2D NormalMap(int size, int baseCells, int octaves, int seed, float strength)
        {
            var height = Fbm(size, baseCells, octaves, seed); var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    float H(int ix, int iy) => height[((iy + size) % size) * size + ((ix + size) % size)];
                    var dx = (H(x + 1, y) - H(x - 1, y)) * strength; var dy = (H(x, y + 1) - H(x, y - 1)) * strength;
                    var n = new Vector3(-dx, -dy, 1f).normalized;
                    pixels[y * size + x] = new Color32((byte)((n.x * .5f + .5f) * 255f), (byte)((n.y * .5f + .5f) * 255f), (byte)((n.z * .5f + .5f) * 255f), 255);
                }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            texture.SetPixels32(pixels); texture.Apply(true); return texture;
        }

        /// <summary>An HDRP detail map that carries only ripples: R = albedo (neutral grey), G = normal Y, B = smoothness (neutral), A = normal X.</summary>
        public static Texture2D DetailNormal(int size, int baseCells, int octaves, int seed, float strength)
        {
            var height = Fbm(size, baseCells, octaves, seed); var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    float H(int ix, int iy) => height[((iy + size) % size) * size + ((ix + size) % size)];
                    var n = new Vector3(-(H(x + 1, y) - H(x - 1, y)) * strength, -(H(x, y + 1) - H(x, y - 1)) * strength, 1f).normalized;
                    pixels[y * size + x] = new Color32(128, (byte)((n.y * .5f + .5f) * 255f), 128, (byte)((n.x * .5f + .5f) * 255f));
                }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            texture.SetPixels32(pixels); texture.Apply(true); return texture;
        }

        /// <summary>A grey detail texture centred on mid-grey (HDRP detail maps multiply by 2 around 0.5), to give the baked ground colour fine grain up close.</summary>
        public static Texture2D DetailGrain(int size, int seed)
        {
            var fine = Fbm(size, 32, 3, seed); var pixels = new Color32[size * size];
            for (var i = 0; i < pixels.Length; i++) { var v = (byte)Mathf.Clamp(128 + (fine[i] - .5f) * 150f, 0, 255); pixels[i] = new Color32(v, 128, v, 255); }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            texture.SetPixels32(pixels); texture.Apply(true); return texture;
        }

        /// <summary>A cloud coverage map for the HDRP cloud layer (red = density).</summary>
        public static Texture2D Clouds(int size, int seed)
        {
            var density = Fbm(size, 4, 5, seed); var pixels = new Color32[size * size];
            for (var i = 0; i < pixels.Length; i++) { var v = (byte)(Mathf.SmoothStep(.35f, .75f, density[i]) * 255f); pixels[i] = new Color32(v, v, v, v); }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            texture.SetPixels32(pixels); texture.Apply(true); return texture;
        }
    }
}
