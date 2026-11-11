using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Items;

namespace LifeVerse.Client.Build;

/// <summary>
/// What the Geral tab shows and changes: identity (name, description), who made it, who owns it, the group, what a click does, whether it is for sale, and what other people
/// and the next owner may do. It reads the selection into a model and turns the person's changes into the commands the server understands; the server decides who may do what.
/// </summary>
public partial class BuildObjectProperties : Node
{
    public static readonly (string Id, string Label)[] ClickActions =
    [
        ("touch", "Tocar (padrão)"), ("sit", "Sentar no objeto"), ("buy", "Comprar objeto"), ("pay", "Pagar objeto"), ("open", "Abrir objeto"), ("zoom", "Zoom no objeto"), ("ignore", "Ignorar objeto"), ("none", "Nenhuma")
    ];

    public static readonly (string Id, string Label)[] SaleModes = [("original", "Vender objeto original"), ("copy", "Vender cópia"), ("contents", "Vender conteúdo")];

    /// <summary>The Geral tab for a selection. A property that differs between the selected objects is null (mixed).</summary>
    public sealed record GeneralModel(
        int Count, bool Mine, string? Name, string? Description, Guid Creator, Guid Owner, Guid LastOwner, Guid? Group,
        string? ClickAction, bool? AnyoneMove, bool? AnyoneCopy, int NextRights, int OwnerRights, string? SaleType, int? Price);

    private BuildNetworkSynchronizer _sync = null!;
    private BuildSelectionManager _selection = null!;

    public void Initialize(BuildNetworkSynchronizer sync, BuildSelectionManager selection) { _sync = sync; _selection = selection; }

    private static JsonObject Perms(BuildObjectState s)
    {
        try { return JsonNode.Parse(string.IsNullOrWhiteSpace(s.PermissionsJson) ? "{}" : s.PermissionsJson) as JsonObject ?? []; } catch (JsonException) { return []; }
    }

    private static bool Flag(JsonObject perms, string key) => perms[key] is JsonValue v && v.TryGetValue<bool>(out var on) && on;
    public static (string Type, int Price) SaleOf(BuildObjectState s)
    {
        var p = Perms(s); var type = p["saleType"] is JsonValue t && t.TryGetValue<string>(out var text) && SaleModes.Any(m => m.Id == text) ? text : "none";
        return (type, p["salePrice"] is JsonValue v && v.TryGetValue<int>(out var price) ? price : 0);
    }

    public GeneralModel? Read()
    {
        var views = _selection.Views().ToList(); if (views.Count == 0) return null;
        T? Same<T>(Func<BuildObjectState, T> pick) where T : struct { var first = pick(views[0].State); return views.All(v => EqualityComparer<T>.Default.Equals(pick(v.State), first)) ? first : null; }
        string? SameText(Func<BuildObjectState, string> pick) { var first = pick(views[0].State); return views.All(v => pick(v.State) == first) ? first : null; }
        var me = _sync.MyUserId; var first = views[0].State;
        var owner = ItemRights.Intersect(views.Select(v => v.State.OwnerRights)); var next = ItemRights.Intersect(views.Select(v => v.State.NextOwnerRights));
        return new GeneralModel(views.Count, views.All(v => v.State.OwnerUserId == me), SameText(s => s.Name), SameText(s => s.Description), first.CreatorUserId, first.OwnerUserId, first.LastOwnerUserId ?? Guid.Empty, views.All(v => v.State.GroupId == first.GroupId) ? first.GroupId : null,
            SameText(s => s.ClickAction), Same(s => Flag(Perms(s), "anyoneMove")), Same(s => Flag(Perms(s), "anyoneCopy")), next, owner, SameText(s => SaleOf(s).Type), Same(s => SaleOf(s).Price));
    }

    // ---- changes ---------------------------------------------------------------------------------------------------

    /// <summary>The name and the description (one object: they belong to it; with several selected they stay as they are).</summary>
    public bool SaveIdentity(string name, string description)
    {
        var views = _selection.Views().ToList();
        if (views.Count != 1) return false;
        name = name.Trim(); if (name.Length is 0 or > 160) return false;
        description = description.Length > 4000 ? description[..4000] : description;
        var state = views[0].State; if (state.Name == name && state.Description == description) return false;
        var after = BuildPersistenceManager.Clone(state); after.Name = name; after.Description = description;
        return _sync.Update(after);
    }

    /// <summary>Sends the changed properties of the whole selection in one command (only the keys in <paramref name="changes"/>).</summary>
    public bool Apply(JsonObject changes) => changes.Count > 0 && _sync.Properties(_selection.Ids, changes);

    public bool SetClickAction(string action) => Apply(new JsonObject { ["clickAction"] = action });

    /// <summary>What the person may mark as sold: the price is a whole number of coins, 0 to 10 000 000, and a sale needs a mode.</summary>
    public static string? SaleProblem(string mode, long price)
    {
        if (!SaleModes.Any(m => m.Id == mode)) return "Escolha o que vende: o original, uma cópia ou o conteúdo.";
        if (price is < 0 or > 10_000_000) return "Preço inválido: use um número inteiro de 0 a 10.000.000.";
        return null;
    }

    public bool PutOnSale(string mode, long price)
    {
        if (SaleProblem(mode, price) is not null) return false;
        return Apply(new JsonObject { ["saleType"] = mode, ["salePrice"] = (int)price });
    }

    public bool TakeOffSale() => Apply(new JsonObject { ["saleType"] = "none" });

    public bool SetAnyone(bool? move, bool? copy)
    {
        var changes = new JsonObject(); if (move is { } m) changes["anyoneMove"] = m; if (copy is { } c) changes["anyoneCopy"] = c;
        return Apply(changes);
    }

    /// <summary>The next owner may modify, copy and transfer as marked; the server never lets it exceed what the owner holds.</summary>
    public bool SetNextOwner(bool modify, bool copy, bool transfer) =>
        Apply(new JsonObject { ["nextOwnerRights"] = (modify ? ItemRights.Modify : 0) | (copy ? ItemRights.Copy : 0) | (transfer ? ItemRights.Transfer : 0) });

    public bool SetGroup(Guid? group) => Apply(new JsonObject { ["groupId"] = group?.ToString() });
}
