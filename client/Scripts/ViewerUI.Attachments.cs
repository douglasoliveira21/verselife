using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts.Attachments;

namespace LifeVerse.Client.Scripts;

public partial class ViewerUI
{
    /// <summary>What the menu of an object worn on the body does (see <see cref="AttachmentActions"/>).</summary>
    private void OnAttachmentAction(string action, AttachmentTarget target)
    {
        var data = GetNodeOrNull<AvatarManager>("/root/AvatarManager")?.Attachments.Items.FirstOrDefault(a => a.Id == target.Id); if (data is null) return;
        AttachmentActions.Run(_world, action, data, text => Toast(text));
    }
}
