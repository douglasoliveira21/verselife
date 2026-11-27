using System;
using System.IO;
using System.Linq;
using LifeVerse.Unity.World;
using LifeVerse.Unity.World.Webp;
using NUnit.Framework;
using UnityEngine;

namespace LifeVerse.Unity.Tests.EditMode
{
    /// <summary>WebP pictures, read in C#: the pixels must be the ones Godot (libwebp) reads from the same files (tests/fixtures/webp, made with Godot before it left the repository).</summary>
    public sealed class WebpTests
    {
        private static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../tests/fixtures/webp"));

        private static (int Width, int Height, byte[] Pixels) Expected(string webp)
        {
            var raw = File.ReadAllBytes(Path.ChangeExtension(webp, ".rgba")); return (BitConverter.ToInt32(raw, 0), BitConverter.ToInt32(raw, 4), raw.Skip(8).ToArray());
        }

        [Test]
        public void EveryReferencePictureComesOutPixelForPixelAsLibwebpGivesIt()
        {
            var files = Directory.GetFiles(Folder, "*.webp"); Assert.GreaterOrEqual(files.Length, 56);
            foreach (var file in files)
            {
                var expected = Expected(file); var picture = WebpDecoder.Decode(File.ReadAllBytes(file)); var name = Path.GetFileName(file);
                Assert.IsNotNull(picture, name); Assert.AreEqual((expected.Width, expected.Height), (picture.Width, picture.Height), name);
                Assert.IsTrue(expected.Pixels.SequenceEqual(picture.Pixels), name);
            }
        }

        [Test]
        public void ADamagedOrForeignFileIsRefusedWithoutThrowing()
        {
            Assert.IsNull(WebpDecoder.Decode(null)); Assert.IsNull(WebpDecoder.Decode(new byte[40])); Assert.IsFalse(WebpDecoder.IsWebp(new byte[] { 1, 2, 3 }));
            var random = new System.Random(7); var files = Directory.GetFiles(Folder, "*.webp").Select(File.ReadAllBytes).ToArray();
            for (var i = 0; i < 1500; i++)
            {
                var bytes = (byte[])files[random.Next(files.Length)].Clone();
                if (random.Next(3) == 0) Array.Resize(ref bytes, random.Next(16, bytes.Length + 1));
                for (var n = random.Next(1, 6); n > 0; n--) bytes[random.Next(12, bytes.Length)] = (byte)random.Next(256);
                Assert.DoesNotThrow(() => WebpDecoder.Decode(bytes), "caso " + i);
            }
            var huge = (byte[])files.First(f => f[15] == (byte)'L').Clone(); huge[21] = 0xFF; huge[22] = 0xFF; huge[23] = 0xFF; huge[24] = 0x0F;   // 16384 x 16384
            Assert.IsNull(WebpDecoder.Decode(huge), "a picture past the limit is not decoded");
        }

        [Test]
        public void ATextureTakesAWebpTheRightWayUpAndKeepsItsMipMaps()
        {
            var file = Path.Combine(Folder, "photo-odd-lossless.webp"); var expected = Expected(file); var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, false);
            try
            {
                Assert.IsTrue(Pictures.Load(texture, File.ReadAllBytes(file))); Assert.AreEqual((expected.Width, expected.Height), (texture.width, texture.height)); Assert.Greater(texture.mipmapCount, 1);
                var top = texture.GetPixel(0, texture.height - 1); var wanted = new Color32(expected.Pixels[0], expected.Pixels[1], expected.Pixels[2], expected.Pixels[3]);
                Assert.AreEqual(wanted, (Color32)top, "the first row of the file is the top of the picture");
                Assert.IsFalse(Pictures.Load(texture, new byte[64]));
                Assert.AreEqual(LifeVerse.Unity.Avatar.AvatarAttachments.ContentKind.Image, LifeVerse.Unity.Avatar.AvatarAttachments.KindOf(File.ReadAllBytes(file)));
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
    }
}
