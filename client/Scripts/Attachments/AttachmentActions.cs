using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts.Attachments;

/// <summary>What can be done to an object worn on the body, the same from the menu on the avatar and from the list in "Meu Avatar": edit it, take it off, move it, see its properties, find its item in the inventory.</summary>
public static class AttachmentActions
{
    /// <summary>Runs one action. <paramref name="world"/> is the node the windows hang from (the world); <paramref name="done"/> is told what to say (a toast, a line in a window).</summary>
    public static void Run(Node world, string action, AttachmentData data, Action<string> say, bool replace = false)
    {
        var store = world.GetNodeOrNull<AvatarManager>("/root/AvatarManager")?.Attachments; if (store is null) return;
        switch (action)
        {
            case "edit": world.GetNodeOrNull<AttachmentEditor>("AttachmentEditor")?.Open(data.Id); break;
            case "detach": store.Detach(data.Id, (ok, error) => say(ok ? "Objeto removido do corpo." : error)); break;
            case "move":
                if (world.GetNodeOrNull<AttachPointWindow>("AttachPointWindow") is { } window)
                    window.Open(data.Name, data.Point, store.WornAt, (point, replaceHere) => store.Move(data.Id, point, replaceHere, (ok, error) => { if (ok) window.Close(); else window.Say(error); }), replace, store.Profiles);
                break;
            case "properties": say(data.Describe()); break;
            case "locate": world.GetNodeOrNull<InventoryPanel>("InventoryPanel")?.Locate(data.InventoryItemId, data.Name); break;
        }
    }
}
