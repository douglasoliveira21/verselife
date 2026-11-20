using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using LifeVerse.Unity.UI;
using NUnit.Framework;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class UpdateDownloadTests
    {
        [Test]
        public void OnlySecureLinksToInstallersAreAllowed()
        {
            Assert.IsTrue(UpdateDownloader.IsAllowed("https://example.com/files/LifeVerse-Setup-0.8.0.exe", out _)); Assert.IsTrue(UpdateDownloader.IsAllowed("https://example.com/a.ZIP", out _)); Assert.IsTrue(UpdateDownloader.IsAllowed("http://127.0.0.1:8080/setup.exe", out _));
            Assert.IsFalse(UpdateDownloader.IsAllowed("http://example.com/setup.exe", out var reason)); StringAssert.Contains("https", reason); Assert.IsFalse(UpdateDownloader.IsAllowed("https://example.com/page", out reason)); StringAssert.Contains("instalador", reason);
            Assert.IsFalse(UpdateDownloader.IsAllowed("file:///c:/windows/calc.exe", out _)); Assert.IsFalse(UpdateDownloader.IsAllowed("not a url", out _)); Assert.IsFalse(UpdateDownloader.IsAllowed("https://example.com/run.bat", out _));
            Assert.AreEqual("LifeVerse-Setup.exe", UpdateDownloader.FileNameOf("https://example.com/x/LifeVerse-Setup.exe?token=1"));
        }

        [Test]
        public void ChecksumsAreComparedWithoutCaseOrSpaces()
        {
            Assert.IsTrue(UpdateDownloader.SameHash("ABCDEF", " abcdef ")); Assert.IsFalse(UpdateDownloader.SameHash("abc", "abd")); Assert.IsFalse(UpdateDownloader.SameHash("", "")); Assert.IsFalse(UpdateDownloader.SameHash(null, "abc"));
            var good = "{\"latest\":\"0.9.0\",\"minimum\":\"0.1.0\",\"downloadUrl\":\"https://example.com/s.exe\",\"sha256\":\"" + new string('A', 64) + "\"}"; var check = new UpdateCheck("0.7.0"); check.Apply(200, good); Assert.AreEqual(new string('a', 64), check.Sha256); Assert.IsTrue(check.CanDownload);
            check.Apply(200, good.Replace(new string('A', 64), "curto")); Assert.AreEqual("", check.Sha256); check.Apply(200, good.Replace("s.exe", "pagina")); Assert.IsFalse(check.CanDownload);
        }

        /// <summary>Runs the download on a pool thread: waiting for it on the editor's own thread would deadlock on its synchronisation context.</summary>
        private static DownloadResult Wait(Func<Task<DownloadResult>> start) => Task.Run(start).GetAwaiter().GetResult();

        private static HttpListener Serve(byte[] bytes, out string url)
        {
            var port = 18000 + new Random().Next(1000); var listener = new HttpListener(); listener.Prefixes.Add("http://127.0.0.1:" + port + "/"); listener.Start(); url = "http://127.0.0.1:" + port + "/setup.exe";
            _ = Task.Run(async () => { try { while (listener.IsListening) { var c = await listener.GetContextAsync(); c.Response.ContentLength64 = bytes.Length; await c.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length); c.Response.Close(); } } catch (Exception) { } });
            return listener;
        }

        [Test]
        public void ADownloadIsSavedAndCheckedAgainstTheSignature()
        {
            var bytes = new byte[200000]; new Random(5).NextBytes(bytes); var folder = Path.Combine(Path.GetTempPath(), "lv-update-" + Guid.NewGuid().ToString("N")); var listener = Serve(bytes, out var url);
            try
            {
                var sha = System.BitConverter.ToString(System.Security.Cryptography.SHA256.Create().ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); float last = 0;
                var ok = Wait(() => UpdateDownloader.DownloadAsync(url, sha, folder, p => last = p)); Assert.IsTrue(ok.Ok, ok.Message); Assert.AreEqual(bytes.Length, new FileInfo(ok.Path).Length); StringAssert.Contains("conferido", ok.Message); Assert.AreEqual(1f, last, .01f);
                var wrong = Wait(() => UpdateDownloader.DownloadAsync(url, new string('0', 64), folder)); Assert.IsFalse(wrong.Ok); StringAssert.Contains("não confere", wrong.Message); Assert.IsFalse(File.Exists(Path.Combine(folder, "setup.exe.part")), "a partial file is never left behind");
                var unsigned = Wait(() => UpdateDownloader.DownloadAsync(url, "", folder)); Assert.IsTrue(unsigned.Ok); StringAssert.Contains("não publicou", unsigned.Message);
                var refused = Wait(() => UpdateDownloader.DownloadAsync("http://example.com/x.exe", "", folder)); Assert.IsFalse(refused.Ok);
            }
            finally { listener.Stop(); try { Directory.Delete(folder, true); } catch (IOException) { } }
        }

        [Test]
        public void OpeningNeedsARealFile() => Assert.IsFalse(UpdateDownloader.Open(Path.Combine(Path.GetTempPath(), "nao-existe-" + Guid.NewGuid() + ".exe")));
    }
}
