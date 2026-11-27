using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class LicenseNoticeTests
    {
        [Test]
        public void EveryLibraryTheGameShipsIsInTheNotices()
        {
            var notices = File.ReadAllText(Path.Combine(Application.dataPath, "LifeVerse", "Legal", "THIRD-PARTY-NOTICES.txt"));
            foreach (var dll in Directory.GetFiles(Path.Combine(Application.dataPath, "LifeVerse", "Plugins"), "*.dll", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(dll); if (name.StartsWith("LifeVerse.")) continue;
                if (name == "MoonSharp.Interpreter") name = "MoonSharp";
                StringAssert.Contains(name, notices, "uma biblioteca nova precisa do aviso de licença dela: " + name);
            }
            foreach (var other in new[] { "libwebp", "Google Inc.", "glTFast", "Apache License", "Inter (fonte)", "SIL OPEN FONT LICENSE", "Tabler Icons" })
                StringAssert.Contains(other.ToLowerInvariant(), notices.ToLowerInvariant());
        }
    }
}
