using System.Threading.Tasks;
using LifeVerse.Unity.World.Webp;
using UnityEngine;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// Puts the bytes of a picture file into a texture: PNG and JPEG with Unity's own reader, WebP with <see cref="WebpDecoder"/> (Unity does not read it). False when the file
    /// cannot be read; the texture is then left as it was. Either way the texture takes the size of the picture and keeps its colour space and mip maps.
    /// </summary>
    public static class Pictures
    {
        public static bool Load(Texture2D texture, byte[] bytes)
        {
            if (texture == null || bytes == null || bytes.Length < 8) return false;
            if (!WebpDecoder.IsWebp(bytes)) return texture.LoadImage(bytes, false);
            return Fill(texture, WebpDecoder.Decode(bytes));
        }

        /// <summary>The same, decoding a WebP away from the main thread (a large one takes a good part of a second).</summary>
        public static async Task<bool> LoadAsync(Texture2D texture, byte[] bytes)
        {
            if (texture == null || bytes == null || bytes.Length < 8) return false;
            if (!WebpDecoder.IsWebp(bytes)) return texture.LoadImage(bytes, false);
            var picture = await Task.Run(() => WebpDecoder.Decode(bytes));
            return texture != null && Fill(texture, picture);
        }

        private static bool Fill(Texture2D texture, WebpPicture picture)
        {
            if (picture == null) return false;
            // Unity keeps the bottom row first
            var line = picture.Width * 4; var turned = new byte[picture.Pixels.Length];
            for (var y = 0; y < picture.Height; y++) System.Buffer.BlockCopy(picture.Pixels, y * line, turned, (picture.Height - 1 - y) * line, line);
            if (!texture.Reinitialize(picture.Width, picture.Height, TextureFormat.RGBA32, texture.mipmapCount > 1)) return false;
            texture.SetPixelData(turned, 0); texture.Apply(true); return true;
        }
    }
}
