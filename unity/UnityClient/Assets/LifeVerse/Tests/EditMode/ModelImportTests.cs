using System;
using System.IO;
using System.Linq;
using System.Text;
using LifeVerse.Unity.Models;
using NUnit.Framework;
using UnityEngine;

namespace LifeVerse.Unity.Tests.EditMode
{
    /// <summary>The pictures of an imported COLLADA model: where they are looked for, the old formats that become PNG, and a whole model with texture, skeleton and animation.</summary>
    public sealed class ModelImportTests
    {
        private string _root, _folder;

        [SetUp]
        public void Make()
        {
            _root = Path.Combine(Path.GetTempPath(), "lv-dae-" + Guid.NewGuid().ToString("N")); _folder = Path.Combine(_root, "modelo"); Directory.CreateDirectory(_folder);
        }

        [TearDown] public void Clean() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

        private string Put(string relative, byte[] bytes)
        {
            var path = Path.GetFullPath(Path.Combine(_folder, relative)); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, bytes); return path;
        }

        [Test]
        public void APictureIsFoundWhereTheFileSaysOrByItsNameInsideTheFolder()
        {
            var wall = Put("textures/parede.png", new byte[] { 1 }); var near = Put("porta.png", new byte[] { 2 }); Put("a/porta.png", new byte[] { 3 });
            Put("1/2/3/4/fundo.png", new byte[] { 4 }); var deep = Put("1/2/3/limite.png", new byte[] { 5 }); Put("../fora.png", new byte[] { 6 });
            string Find(string reference) => DaeUploadPackager.FindTexture(_folder, reference);
            Assert.AreEqual(wall, Find("textures/parede.png")); Assert.AreEqual(wall, Find("./textures/parede.png")); Assert.AreEqual(wall, Find("textures\\parede.png"));
            Assert.AreEqual(wall, Find("parede.png"), "only the name, the picture in a folder inside"); Assert.AreEqual(wall, Find("PAREDE.PNG"));
            Assert.AreEqual(wall, Find("file:///C:/Users/outra/Documents/parede.png"), "the path of another computer"); Assert.AreEqual(wall, Find("C:\\Users\\outra\\parede.png")); Assert.AreEqual(wall, Find("/home/outra/parede.png"));
            Assert.AreEqual(wall, Find("imagens/parede.png"), "a folder that was renamed"); Assert.AreEqual(wall, Find("textures/%70arede.png"));
            Assert.AreEqual(near, Find("porta.png"), "the nearest one wins"); Assert.AreEqual(near, Find("../porta.png"));
            Assert.AreEqual(deep, Find("limite.png"), "three folders down");
            Assert.IsNull(Find("fundo.png"), "four folders down is too far");
            Assert.IsNull(Find("../fora.png"), "nothing outside the folder of the model"); Assert.IsNull(Find("fora.png")); Assert.IsNull(Find(Path.Combine(_root, "fora.png")));
            Assert.IsNull(Find("http://exemplo.test/parede.png"), "nothing from the internet"); Assert.IsNull(Find("data:image/png;base64,AAAA")); Assert.IsNull(Find("")); Assert.IsNull(Find("..")); Assert.IsNull(Find("nao-existe.png"));
        }

        // a 2 by 2 picture: red and green on top, blue and white below
        private static readonly Color32[] Wanted = { new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 255), new Color32(0, 0, 255, 255), new Color32(255, 255, 255, 255) };

        private static byte[] Tga(int type, int bits, bool topFirst, bool packed)
        {
            var file = new MemoryStream(); var header = new byte[18]; header[1] = (byte)(type == 1 ? 1 : 0); header[2] = (byte)(packed ? type + 8 : type);
            if (type == 1) { header[5] = 4; header[7] = 24; }
            header[12] = 2; header[14] = 2; header[16] = (byte)bits; header[17] = (byte)((topFirst ? 0x20 : 0) | (bits == 32 ? 8 : 0)); file.Write(header, 0, 18);
            if (type == 1) foreach (var c in Wanted) { file.WriteByte(c.b); file.WriteByte(c.g); file.WriteByte(c.r); }
            var order = topFirst ? new[] { 0, 1, 2, 3 } : new[] { 2, 3, 0, 1 };
            foreach (var i in order)
            {
                if (packed) file.WriteByte(0);   // a packet of one raw pixel
                if (type == 1) { file.WriteByte((byte)i); continue; }
                var c = Wanted[i]; file.WriteByte(c.b); file.WriteByte(c.g); file.WriteByte(c.r); if (bits == 32) file.WriteByte(255);
            }
            return file.ToArray();
        }

        private static byte[] Bmp(int bits, bool topFirst)
        {
            var stride = (2 * bits + 31) / 32 * 4; var palette = bits == 8 ? 16 : 0; var data = new byte[54 + palette + stride * 2];
            void I(int at, int v) { for (var k = 0; k < 4; k++) data[at + k] = (byte)(v >> (8 * k)); }
            data[0] = (byte)'B'; data[1] = (byte)'M'; I(2, data.Length); I(10, 54 + palette); I(14, 40); I(18, 2); I(22, topFirst ? -2 : 2); data[26] = 1; data[28] = (byte)bits; if (bits == 8) I(46, 4);
            if (bits == 8) for (var i = 0; i < 4; i++) { data[54 + i * 4] = Wanted[i].b; data[55 + i * 4] = Wanted[i].g; data[56 + i * 4] = Wanted[i].r; }
            for (var y = 0; y < 2; y++)
                for (var x = 0; x < 2; x++)
                {
                    var source = (topFirst ? y : 1 - y) * 2 + x; var at = 54 + palette + y * stride + x * bits / 8;
                    if (bits == 8) data[at] = (byte)source; else { data[at] = Wanted[source].b; data[at + 1] = Wanted[source].g; data[at + 2] = Wanted[source].r; }   // 32 bits with an empty alpha: opaque
                }
            return data;
        }

        private static void Check(byte[] source, string extension, string what)
        {
            Assert.IsTrue(PictureConverter.TryToPng(source, extension, out var png), what);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(texture.LoadImage(png), what + ": the PNG reads"); Assert.AreEqual(2, texture.width); Assert.AreEqual(2, texture.height);
                for (var i = 0; i < 4; i++) Assert.AreEqual(Wanted[i], (Color32)texture.GetPixel(i % 2, 1 - i / 2), what + " pixel " + i);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        [Test]
        public void TgaAndBmpBecomePngUpright()
        {
            Check(Tga(2, 24, false, false), ".tga", "24 bits"); Check(Tga(2, 32, true, false), ".tga", "32 bits, top first"); Check(Tga(2, 24, false, true), ".TGA", "run-length packed"); Check(Tga(1, 8, false, false), ".tga", "palette");
            Check(Bmp(24, false), ".bmp", "BMP 24"); Check(Bmp(32, true), ".bmp", "BMP 32, top first"); Check(Bmp(8, false), ".bmp", "BMP with a palette");
        }

        [Test]
        public void ABrokenOrMadeUpPictureIsRefused()
        {
            Assert.IsFalse(PictureConverter.TryToPng(new byte[] { 1, 2, 3 }, ".tga", out _)); Assert.IsFalse(PictureConverter.TryToPng(Encoding.ASCII.GetBytes("not a picture at all, just some words to fill the header up to size"), ".bmp", out _));
            var cut = Tga(2, 24, false, false); Array.Resize(ref cut, cut.Length - 5); Assert.IsFalse(PictureConverter.TryToPng(cut, ".tga", out _), "cut short");
            var huge = Tga(2, 24, false, false); huge[12] = 0xFF; huge[13] = 0xFF; huge[14] = 0xFF; huge[15] = 0xFF; Assert.IsFalse(PictureConverter.TryToPng(huge, ".tga", out _), "an absurd size");
            var packedCut = Tga(2, 24, false, true); Array.Resize(ref packedCut, 20); Assert.IsFalse(PictureConverter.TryToPng(packedCut, ".tga", out _));
        }

        private const string Dae = @"<?xml version=""1.0"" encoding=""utf-8""?>
<COLLADA xmlns=""http://www.collada.org/2005/11/COLLADASchema"" version=""1.4.1"">
 <asset><unit meter=""0.01""/><up_axis>Z_UP</up_axis></asset>
 <library_images><image id=""img""><init_from>file:///C:/Users/outra/Documents/pintura.tga</init_from></image></library_images>
 <library_effects><effect id=""fx""><profile_COMMON><technique sid=""c""><lambert><diffuse><texture texture=""img"" texcoord=""UV""/></diffuse></lambert></technique></profile_COMMON></effect></library_effects>
 <library_materials><material id=""mat""><instance_effect url=""#fx""/></material></library_materials>
 <library_geometries><geometry id=""geo""><mesh>
  <source id=""pos""><float_array id=""pa"" count=""9"">0 0 0 100 0 0 0 0 100</float_array><technique_common><accessor source=""#pa"" count=""3"" stride=""3""/></technique_common></source>
  <source id=""uv""><float_array id=""ua"" count=""6"">0 0 1 0 0 1</float_array><technique_common><accessor source=""#ua"" count=""3"" stride=""2""/></technique_common></source>
  <vertices id=""ver""><input semantic=""POSITION"" source=""#pos""/></vertices>
  <triangles material=""m"" count=""1""><input semantic=""VERTEX"" source=""#ver"" offset=""0""/><input semantic=""TEXCOORD"" source=""#uv"" offset=""1""/><p>0 0 1 1 2 2</p></triangles>
 </mesh></geometry></library_geometries>
 <library_controllers><controller id=""ctl""><skin source=""#geo"">
  <source id=""jn""><Name_array id=""ja"" count=""1"">osso</Name_array><technique_common><accessor source=""#ja"" count=""1"" stride=""1""/></technique_common></source>
  <source id=""ib""><float_array id=""ia"" count=""16"">1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1</float_array><technique_common><accessor source=""#ia"" count=""1"" stride=""16""/></technique_common></source>
  <source id=""we""><float_array id=""wa"" count=""1"">1</float_array><technique_common><accessor source=""#wa"" count=""1"" stride=""1""/></technique_common></source>
  <joints><input semantic=""JOINT"" source=""#jn""/><input semantic=""INV_BIND_MATRIX"" source=""#ib""/></joints>
  <vertex_weights count=""3""><input semantic=""JOINT"" source=""#jn"" offset=""0""/><input semantic=""WEIGHT"" source=""#we"" offset=""1""/><vcount>1 1 1</vcount><v>0 0 0 0 0 0</v></vertex_weights>
 </skin></controller></library_controllers>
 <library_animations><animation id=""a"" name=""Girar"">
  <source id=""at""><float_array id=""ata"" count=""2"">0 1</float_array><technique_common><accessor source=""#ata"" count=""2"" stride=""1""/></technique_common></source>
  <source id=""av""><float_array id=""ava"" count=""2"">0 90</float_array><technique_common><accessor source=""#ava"" count=""2"" stride=""1""/></technique_common></source>
  <sampler id=""as""><input semantic=""INPUT"" source=""#at""/><input semantic=""OUTPUT"" source=""#av""/></sampler><channel source=""#as"" target=""osso/giro.ANGLE""/>
 </animation></library_animations>
 <library_visual_scenes><visual_scene id=""s""><node id=""osso"" sid=""osso"" type=""JOINT""><rotate sid=""giro"">0 1 0 0</rotate></node><node id=""corpo""><instance_controller url=""#ctl""><bind_material><technique_common><instance_material symbol=""m"" target=""#mat""/></technique_common></bind_material></instance_controller></node></visual_scene></library_visual_scenes>
 <scene><instance_visual_scene url=""#s""/></scene>
</COLLADA>";

        [Test]
        public void AColladaWithItsPictureSkeletonAndAnimationBecomesOneFile()
        {
            Put("texturas/pintura.tga", Tga(2, 24, false, false)); var path = Put("peca.dae", Encoding.UTF8.GetBytes(Dae));
            Assert.IsTrue(DaeUploadPackager.TryPack(path, File.ReadAllBytes(path), out var glb, out var error, out var warnings), error);
            CollectionAssert.IsEmpty(warnings, string.Join(" | ", warnings));
            var report = ModelInspector.Inspect(glb); Assert.IsTrue(report.Valid, report.Error); Assert.AreEqual(1, report.Triangles); Assert.AreEqual(1, report.Textures); Assert.AreEqual(1, report.Animations);
            var json = Encoding.UTF8.GetString(glb, 20, BitConverter.ToInt32(glb, 12));
            StringAssert.Contains("\"image/png\"", json, "the TGA went as PNG"); StringAssert.Contains("\"skins\"", json); StringAssert.Contains("\"Girar\"", json);
        }

        [Test]
        public void APictureThatIsNotThereLeavesAWarningAndTheModelStillComes()
        {
            var path = Put("peca.dae", Encoding.UTF8.GetBytes(Dae));
            Assert.IsTrue(DaeUploadPackager.TryPack(path, File.ReadAllBytes(path), out var glb, out var error, out var warnings), error);
            Assert.AreEqual(1, warnings.Count); StringAssert.Contains("pintura.tga", warnings.Single()); Assert.AreEqual(0, ModelInspector.Inspect(glb).Textures);
        }
    }
}
