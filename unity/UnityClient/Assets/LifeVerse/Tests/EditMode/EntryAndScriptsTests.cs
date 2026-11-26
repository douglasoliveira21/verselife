using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using LifeVerse.Unity.UI;
using LifeVerse.Unity.World;
using NUnit.Framework;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class EntryAndScriptsTests
    {
        private static string Hash(byte[] bytes) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static string Folder() => Path.Combine(Path.GetTempPath(), "lv-assets-" + Guid.NewGuid().ToString("N"));

        [Test]
        public void TheManifestIsRead()
        {
            var a = Guid.NewGuid(); var list = AssetPreloader.ParseManifest("{\"assets\":[{\"assetId\":\"" + a + "\",\"kind\":\"model\",\"hash\":\"ab\",\"size\":10,\"mime\":\"model/gltf-binary\",\"distance\":4.5},{\"assetId\":\"x\"}]}");
            Assert.AreEqual(1, list.Count); Assert.AreEqual(a, list[0].AssetId); Assert.AreEqual(LifeVerse.Shared.Loading.AssetKind.Model, list[0].Kind); Assert.AreEqual(10, list[0].Size); Assert.AreEqual(4.5, list[0].Distance);
            StringAssert.EndsWith("/assets/manifest", AssetPreloader.ManifestPath(a, null, null)); StringAssert.EndsWith("?x=1.5&z=-2", AssetPreloader.ManifestPath(a, 1.5f, -2f));
            Assert.AreEqual("Conferindo os recursos da ilha…", AssetPreloader.Describe(0, 0)); StringAssert.Contains("1,0 de 2,0 MB", AssetPreloader.Describe(1048576, 2097152));
        }

        [Test]
        public void FilesAreKeptByHashAndCheckedAgain()
        {
            var folder = Folder(); var bytes = new byte[] { 1, 2, 3, 4 }; var hash = Hash(bytes); var downloads = 0; var id = Guid.NewGuid(); var bad = Guid.NewGuid();
            var preload = new AssetPreloader(folder, asset => { downloads++; return Task.FromResult(asset == id ? bytes : new byte[] { 9 }); });
            try
            {
                var manifest = "{\"assets\":[{\"assetId\":\"" + id + "\",\"kind\":\"texture\",\"hash\":\"" + hash + "\",\"size\":4,\"mime\":\"image/png\",\"distance\":1}]}";
                Assert.IsTrue(preload.Essentials(200, manifest).Result); Assert.AreEqual(1, preload.Downloads); Assert.AreEqual(4, preload.DoneBytes); Assert.IsNotNull(preload.TryRead(hash));
                Assert.IsTrue(preload.Essentials(200, manifest).Result); Assert.AreEqual(1, downloads, "the second time the file comes from the disk"); Assert.AreEqual(1, preload.CacheHits);
                File.WriteAllBytes(Path.Combine(folder, hash + ".bin"), new byte[] { 7 }); Assert.IsNull(preload.TryRead(hash), "a damaged file is not used"); Assert.IsFalse(File.Exists(Path.Combine(folder, hash + ".bin")));
                CollectionAssert.AreEqual(bytes, preload.Load(id).Result); Assert.IsNotNull(preload.TryRead(hash), "what an object fetched is kept again");
                var wrong = "{\"assets\":[{\"assetId\":\"" + bad + "\",\"kind\":\"texture\",\"hash\":\"" + hash + "\",\"size\":4,\"mime\":\"image/png\",\"distance\":1}]}"; File.Delete(Path.Combine(folder, hash + ".bin"));
                Assert.IsFalse(preload.Essentials(200, wrong).Result); Assert.AreEqual(2, preload.Refused, "a download with the wrong hash is refused and tried once more"); StringAssert.Contains("imagem", preload.LastError);
                Assert.IsTrue(preload.Essentials(404, "").Result, "an older server without the list still lets the person in"); Assert.IsTrue(preload.Essentials(500, "").Result); Assert.IsNotNull(preload.LastError);
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Test]
        public void TheCacheIsPrunedOldestFirst()
        {
            var folder = Folder(); Directory.CreateDirectory(folder);
            try
            {
                for (var i = 0; i < 5; i++) { var p = Path.Combine(folder, i + ".bin"); File.WriteAllBytes(p, new byte[100]); File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddMinutes(i)); }
                Assert.AreEqual(0, AssetPreloader.Prune(folder, 1000)); Assert.AreEqual(2, AssetPreloader.Prune(folder, 400)); Assert.IsFalse(File.Exists(Path.Combine(folder, "0.bin"))); Assert.IsTrue(File.Exists(Path.Combine(folder, "4.bin")));
            }
            finally { Directory.Delete(folder, true); }
        }

        [Test]
        public void TheServerMayChooseTheIsland()
        {
            Assert.IsNull(LoginView.Validate(false, "ana", "", "senha", "", out var id)); Assert.AreEqual(Guid.Empty, id);
            Assert.IsNotNull(LoginView.Validate(false, "ana", "", "senha", "xyz", out _));
        }

        [Test]
        public void CodeIsCompletedFromTheReference()
        {
            var reference = LuaEditorRules.ReadReference("{\"functions\":[{\"namespace\":\"object\",\"name\":\"say\",\"signature\":\"object.say(text)\"},{\"namespace\":\"object\",\"name\":\"set_color\"},{\"namespace\":\"timer\",\"name\":\"start\"}],\"events\":[{\"name\":\"on_touch\"},{\"name\":\"on_timer\"}]}");
            CollectionAssert.AreEqual(new[] { "say(" }, LuaEditorRules.CompletionFor("  object.s", reference, "").Where(c => c.Shown == "say").Select(c => c.Insert).ToArray());
            Assert.AreEqual(2, LuaEditorRules.CompletionFor("object.", reference, "").Count);
            var words = LuaEditorRules.CompletionFor("on_t", reference, "").Select(c => c.Shown).ToList(); CollectionAssert.AreEquivalent(new[] { "on_touch", "on_timer" }, words);
            Assert.IsTrue(LuaEditorRules.CompletionFor("fu", null, "").Any(c => c.Shown == "function")); Assert.IsTrue(LuaEditorRules.CompletionFor("con", null, "local contador = 0\n").Any(c => c.Shown == "contador"));
            Assert.AreEqual(0, LuaEditorRules.CompletionFor("", reference, "").Count); Assert.AreEqual("sa", LuaEditorRules.Typed("object.sa"));
        }

        [Test]
        public void LinesAndBreakpoints()
        {
            var text = "a\nbb\nccc"; Assert.AreEqual(1, LuaEditorRules.LineOf(text, 0)); Assert.AreEqual(2, LuaEditorRules.LineOf(text, 3)); Assert.AreEqual(3, LuaEditorRules.LineCount(text));
            Assert.AreEqual((2, 4), LuaEditorRules.LineRange(text, 2)); Assert.AreEqual((5, 8), LuaEditorRules.LineRange(text, 3)); Assert.AreEqual((-1, -1), LuaEditorRules.LineRange(text, 4));
            CollectionAssert.AreEqual(new[] { 1, 3 }, LuaEditorRules.ParseLines("3, 1 3 9 x", 3)); CollectionAssert.AreEqual(new[] { 1, 2, 3 }, LuaEditorRules.Toggle(new[] { 1, 3 }, 2)); CollectionAssert.AreEqual(new[] { 1 }, LuaEditorRules.Toggle(new[] { 1, 3 }, 3));
            var script = Guid.NewGuid(); var command = LuaEditorRules.DebugCommand(script, "start", new[] { 2, 5 }); StringAssert.Contains("\"debug\":true", command); StringAssert.Contains("\"action\":\"start\"", command); StringAssert.Contains("\"breakpoints\":[2,5]", command);
        }

        [Test]
        public void WhatTheIslandSaysWhileDebugging()
        {
            var script = Guid.NewGuid();
            var stopped = LuaEditorRules.ReadDebug("{\"operation\":\"script_debug\",\"script\":\"" + script + "\",\"event\":\"stopped\",\"debugging\":true,\"stopped\":true,\"line\":4,\"reason\":\"breakpoint\",\"handler\":\"on_touch\",\"locals\":[{\"name\":\"n\",\"value\":\"3\",\"type\":\"number\"}],\"globals\":[]}", script);
            Assert.IsTrue(stopped.Stopped && stopped.Debugging); Assert.AreEqual(4, stopped.Line); StringAssert.Contains("Parado na linha 4 (ponto de parada) · evento on_touch", stopped.Text); StringAssert.Contains("n = 3  (number)", stopped.Text); StringAssert.Contains("(nenhuma)", stopped.Text);
            Assert.IsNull(LuaEditorRules.ReadDebug("{\"operation\":\"script_debug\",\"script\":\"" + Guid.NewGuid() + "\",\"event\":\"started\"}", script), "another script is ignored");
            Assert.IsNull(LuaEditorRules.ReadDebug("{\"operation\":\"script_dialog\"}", script)); Assert.AreEqual("ended", LuaEditorRules.ReadDebug("{\"operation\":\"script_debug\",\"script\":\"" + script + "\",\"event\":\"ended\",\"reason\":\"timeout\"}", script).Kind);
        }

        [Test]
        public void HowAScriptIsDoing()
        {
            var r = LuaEditorRules.ReadRuntime("{\"status\":\"paused\",\"version\":3,\"runtime\":{\"state\":\"Paused\",\"stateText\":\"pausado\",\"events\":1200,\"errors\":2,\"apiCalls\":5,\"instructions\":90000,\"allocatedBytes\":2048,\"cpuMilliseconds\":1.5,\"lastError\":\"x é nil\",\"lastErrorLine\":7},\"logs\":[{\"level\":\"error\",\"eventName\":\"on_touch\",\"text\":\"falhou\",\"stackTrace\":\"linha 7\",\"createdAt\":\"2026-11-26T10:20:30Z\"}],\"grants\":[{\"subjectId\":\"" + Guid.Empty + "\",\"permission\":\"HTTP_REQUESTS\"}]}");
            Assert.IsTrue(r.Reported); Assert.AreEqual("paused", r.Status); Assert.AreEqual(1, r.Logs.Count); Assert.AreEqual(1, r.Grants.Count);
            StringAssert.Contains("10:20:30  ERRO", LuaEditorRules.FormatLog(r.Logs[0])); StringAssert.Contains("↳ linha 7", LuaEditorRules.FormatLog(r.Logs[0]));
            var text = LuaEditorRules.Performance(r); StringAssert.Contains("pausado", text); StringAssert.Contains("1.200", text); StringAssert.Contains("linha 7", text);
            Assert.IsFalse(LuaEditorRules.ReadRuntime("{\"status\":\"active\"}").Reported); StringAssert.Contains("30 segundos", LuaEditorRules.Performance(LuaEditorRules.ReadRuntime("{}")));
            var (valid, problems, handlers) = LuaEditorRules.ReadCheck("{\"valid\":false,\"errors\":[{\"line\":3,\"message\":\"'end' esperado\"}],\"warnings\":[{\"line\":1,\"message\":\"evento desconhecido\"}],\"handlers\":[\"on_touch\"]}");
            Assert.IsFalse(valid); Assert.AreEqual(2, problems.Count); Assert.AreEqual(("error", 3, "'end' esperado"), problems[0]); CollectionAssert.AreEqual(new[] { "on_touch" }, handlers);
        }
    }
}
