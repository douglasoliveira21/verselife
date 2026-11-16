using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts.Attachments;

namespace LifeVerse.Client.Scripts;

/// <summary>Putting an object of the inventory on the body: where (the window of points), whether to add or replace, taking it off and moving it to another point. The server decides; this only asks.</summary>
public partial class InventoryPanel
{
    private AttachPointWindow? _pointWindow;
    public AttachPointWindow? PointWindowForTesting => _pointWindow;

    private AttachmentStore? Store => GetNodeOrNull<AvatarManager>("/root/AvatarManager")?.Attachments;

    private AttachPointWindow PointWindow()
    {
        if (_pointWindow is not null && IsInstanceValid(_pointWindow)) return _pointWindow;
        _pointWindow = GetParent()?.GetNodeOrNull<AttachPointWindow>("AttachPointWindow");
        if (_pointWindow is null) { _pointWindow = new AttachPointWindow { Name = "AttachPointWindow" }; (GetParent() ?? this).AddChild(_pointWindow); }
        return _pointWindow;
    }

    private bool TrySelectedModel(out ItemInfo item)
    {
        item = null!;
        if (_selectedItem == Guid.Empty || !_items.TryGetValue(_selectedItem, out var found)) return false;
        if (!IsAttachable(found)) { _status.Text = "Só modelos 3D, imagens e construções podem ser anexados ao corpo."; return false; }
        item = found; return true;
    }

    private void Report(bool ok, string error) { if (!ok) { _status.Text = error; PointWindow().Say(error); } else PointWindow().Close(); }

    /// <summary>"Anexar em…": the window of points; the choice attaches the item there (or, when it is already worn, moves it).</summary>
    public void AttachTo(bool replace = false)
    {
        if (!TrySelectedModel(out var item) || Store is not { } store) return;
        var worn = store.ForItem(item.ItemId) is { Equipped: true } row ? row : null;
        PointWindow().Open(item.Name, worn?.Point ?? string.Empty, store.WornAt, (point, replaceHere) =>
        {
            if (worn is not null) store.Move(worn.Id, point, replaceHere, Report); else store.Attach(item.ItemId, point, replaceHere, Report);
        }, replace, store.Profiles);
    }

    /// <summary>An item that has just been saved (from the world): the window of points, and the choice puts it there.</summary>
    public void AttachNew(Guid itemId, string name)
    {
        if (Store is not { } store) return;
        PointWindow().Open(name, string.Empty, store.WornAt, (point, replaceHere) => store.Attach(itemId, point, replaceHere, Report), false, store.Profiles);
    }

    /// <summary>"Adicionar ao corpo": the point the item had last time (keeping what is there), or the window when it never was worn.</summary>
    public void AttachAdd()
    {
        if (!TrySelectedModel(out var item) || Store is not { } store) return;
        if (store.ForItem(item.ItemId) is { Equipped: false } before) { store.Attach(item.ItemId, before.Point, false, (ok, error) => { if (!ok) _status.Text = error; }); return; }
        if (store.IsWorn(item.ItemId)) { _status.Text = "Este objeto já está no corpo."; return; }
        AttachTo();
    }

    public void Detach()
    {
        if (_selectedItem == Guid.Empty || Store is not { } store || store.ForItem(_selectedItem) is not { Equipped: true } row) { _status.Text = "Este objeto não está no corpo."; return; }
        store.Detach(row.Id, (ok, error) => _status.Text = ok ? "Objeto removido do corpo." : error);
    }
}

public partial class InventoryPanel
{
    private Guid _locate;

    /// <summary>Opens the inventory with a search for the item's name and selects the item when the list arrives.</summary>
    public void Locate(Guid itemId, string name)
    {
        _locate = itemId; _window.Visible = true; _window.MoveToFront();
        _search.Text = name; LoadItems();
    }

    /// <summary>"Editar anexo": the editor of the placement, on the selected item when it is worn.</summary>
    public void EditAttachment()
    {
        if (_selectedItem == Guid.Empty || Store is not { } store || store.ForItem(_selectedItem) is not { Equipped: true } row) { _status.Text = "Este objeto não está no corpo."; return; }
        (GetParent() ?? this).GetNodeOrNull<AttachmentEditor>("AttachmentEditor")?.Open(row.Id);
    }
}
