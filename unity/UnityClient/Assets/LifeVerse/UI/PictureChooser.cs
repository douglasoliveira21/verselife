using System;
using System.IO;

namespace LifeVerse.Unity.UI
{
    /// <summary>Giving a profile, a group or an island a picture: a file of this computer (it goes to the inventory first) or an image that is already there.</summary>
    public static class PictureChooser
    {
        public const long MaxBytes = 8 * 1024 * 1024;
        public const string Formats = "PNG, JPG ou WebP, até 8 MB.";

        /// <summary>Asks for a file (or takes the one in <paramref name="path"/>), sends it to the inventory and hands over its asset; <paramref name="refused"/> gets the reason when it cannot.</summary>
        public static void FromComputer(ApiContext context, string title, Action<Guid> chosen, Action<string> refused, string path = null)
        {
            void Send(string file)
            {
                long size;
                try { size = new FileInfo(file).Length; }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) { refused("Não foi possível ler o arquivo: " + exception.Message); return; }
                if (size > MaxBytes) { refused("A imagem passa de 8 MB."); return; }
                Uploads.Send(context, file, "textures", result => { if (result.Ok && result.AssetId != Guid.Empty) chosen(result.AssetId); else refused(result.Message); });
            }
            if (path != null) { Send(path); return; }
            new FilePickerWindow(context.Ui, title, Uploads.ImageFilter, Send, Directory.Exists(DiscoveryWindow.PhotoFolder) ? DiscoveryWindow.PhotoFolder : null).Open();
        }

        /// <summary>Asks for one of the images of the inventory.</summary>
        public static TexturePickerWindow FromInventory(ApiContext context, string title, Action<Guid> chosen)
        {
            var window = new TexturePickerWindow(context, (builtin, id) => { if (!builtin && Guid.TryParse(id, out var asset)) chosen(asset); }, title, false);
            window.Open(); return window;
        }
    }
}
