using System;
using LifeVerse.Shared.Items;
using LifeVerse.Unity.UI;
using NUnit.Framework;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class InventoryManagerTests
    {
        [Test]
        public void ListAddressesCarryTheViewFolderSearchAndOrder()
        {
            Assert.AreEqual("/api/v1/inventory/items?view=all&page=1&pageSize=30", InventoryManagerWindow.ListPath("all", null, "", "", 1));
            Assert.AreEqual("/api/v1/inventory/items?view=all&page=2&pageSize=30&unfiled=true", InventoryManagerWindow.ListPath("unfiled", null, null, null, 2));
            var folder = Guid.NewGuid(); var path = InventoryManagerWindow.ListPath("favorites", folder, " casa azul ", "name", 0);
            StringAssert.StartsWith("/api/v1/inventory/items?view=favorites&page=1&", path); StringAssert.Contains("&folderId=" + folder, path); StringAssert.Contains("&search=casa%20azul", path); StringAssert.EndsWith("&sort=name", path);
        }

        [Test]
        public void SizesAndRightsAreWords()
        {
            Assert.AreEqual("512 B", InventoryManagerWindow.Size(512)); Assert.AreEqual("2,0 KB", InventoryManagerWindow.Size(2048)); Assert.AreEqual("12,0 MB", InventoryManagerWindow.Size(12L * 1024 * 1024)); Assert.AreEqual("1,50 GB", InventoryManagerWindow.Size(3L * 512 * 1024 * 1024));
            Assert.AreEqual("sem direitos", InventoryManagerWindow.RightsWords(0)); Assert.AreEqual("copiar, transferir", InventoryManagerWindow.RightsWords(ItemRights.Copy | ItemRights.Transfer)); Assert.AreEqual("copiar, modificar, transferir, revender", InventoryManagerWindow.RightsWords(ItemRights.All));
            Assert.AreEqual("textura", InventoryManagerWindow.CategoryWords("textures")); Assert.AreEqual("xyz", InventoryManagerWindow.CategoryWords("xyz"));
        }

        [Test]
        public void RefusalsAndPacksAreWords()
        {
            Assert.AreEqual("Já existe uma pasta com esse nome aqui.", InventoryManagerWindow.Words(409, "{\"error\":\"name_taken\"}"));
            Assert.AreEqual("3 item(ns) na pasta Texturas.", InventoryManagerWindow.PackWords(200, "{\"folderId\":\"" + Guid.NewGuid() + "\",\"folderName\":\"Texturas\",\"count\":3,\"items\":[]}"));
            var bad = InventoryManagerWindow.PackWords(422, "{\"error\":\"bad_files\",\"report\":{\"problems\":[{\"file\":\"virus.exe\",\"error\":\"tipo não aceito\"}]}}");
            StringAssert.StartsWith("Alguns arquivos", bad); StringAssert.Contains("virus.exe: tipo não aceito", bad);
            Assert.AreEqual("O .zip está vazio.", InventoryManagerWindow.PackWords(422, "{\"error\":\"empty_zip\"}"));
            Assert.AreEqual(11, InventoryManagerWindow.Sections.Length);
        }
    }
}
