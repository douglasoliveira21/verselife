using System.Text.Json;
using Godot;
using LifeVerse.Client.Build;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Items;

namespace LifeVerse.Client.Tests;

/// <summary>The new editor, phase 4: the GERAL tab changes the real properties (identity, click action, sale, permissions) through the server and shows what the server says.</summary>
public partial class BuildGeneralSmoke : Node3D
{
    private static readonly Guid Ana = Guid.NewGuid(), Bruno = Guid.NewGuid(), Carla = Guid.NewGuid();

    private static object StateOf(Guid id, string name, int revision, Guid owner, string permissions = "{}", string click = "touch", Guid? last = null, int ownerRights = 15, int nextRights = 15, float x = 0) =>
        new { id, islandId = Guid.NewGuid(), creatorUserId = Ana, ownerUserId = owner, lastOwnerUserId = last, name, description = "uma caixa", primitiveType = "cube", positionX = x, positionY = .5f, positionZ = 0, rotationX = 0, rotationY = 0, rotationZ = 0, scaleX = 1, scaleY = 1, scaleZ = 1, materialJson = "{}", geometryJson = "{}", physicsJson = "{\"collision\":true}", permissionsJson = permissions, contentJson = "[]", revision, cost = 3, clickAction = click, ownerRights, nextOwnerRights = nextRights };

    private static string Op(string operation, object state) => JsonSerializer.Serialize(new { operation, objectState = state });
    private static string Props(params object[] states) => JsonSerializer.Serialize(new { operation = "properties", objects = states });
    private static JsonElement Last(List<string> commands) => JsonDocument.Parse(commands[^1]).RootElement.Clone();

    public override async void _Ready()
    {
        try
        {
            var camera = new Camera3D { Current = true, Position = new Vector3(0, 8, 8) }; AddChild(camera); camera.LookAt(Vector3.Zero);
            var editor = new BuildEditorController { Name = "BuildEditorController" }; AddChild(editor);
            var commands = new List<string>(); editor.Synchronizer.OperationSink = json => { if (!json.StartsWith("{\"operation\":\"lock\"") && !json.StartsWith("{\"operation\":\"unlock\"") && !json.StartsWith("{\"operation\":\"live\"")) commands.Add(json); return true; };
            editor.Names.LookupOverride = id => id == Ana ? "Ana Souza" : id == Bruno ? "Bruno Lima" : id == Carla ? "Carla" : null;
            await Frames(3); editor.SetActive(true);
            var tab = editor.Window.General; var id = Guid.NewGuid(); var other = Guid.NewGuid();

            Require(editor.Window.TabNamesForTesting.SequenceEqual(["GERAL", "OBJETO", "RECURSOS", "TEXTURA", "CONTEÚDO"]), "only the tabs that work are shown");
            Require(!editor.Window.TabsVisibleForTesting, "no tab without a selection");
            editor.Synchronizer.OnOperation(Op("create", StateOf(id, "Caixa", 1, Guid.Empty, last: Bruno)));
            editor.Selection.Set([id]); await Frames(2);
            Require(editor.Window.TabsVisibleForTesting && tab.EditableForTesting, "the tab shows for the selected object, and it is editable because it is mine");
            Require(tab.NameForTesting == "Caixa" && tab.DescriptionForTesting == "uma caixa", "name and description");
            Require(tab.CreatorForTesting == "Ana Souza" && tab.LastOwnerForTesting == "Bruno Lima", $"the creator and the previous owner are shown by name ({tab.CreatorForTesting} / {tab.LastOwnerForTesting})");
            Require(tab.ClickLabelsForTesting.Count == 8 && tab.ClickLabelsForTesting[0].StartsWith("Tocar") && tab.ClickLabelsForTesting[7] == "Nenhuma", "the eight click actions");
            Require(tab.ClickForTesting == "touch" && !tab.ForSaleForTesting && !tab.SaleRowsVisibleForTesting, "touch by default and not for sale");

            // identity
            tab.NameForTesting = "Porta da casa"; commands.Clear(); tab.CommitNameForTesting();
            var rename = Last(commands);
            Require(rename.GetProperty("operation").GetString() == "update" && rename.GetProperty("object").GetProperty("name").GetString() == "Porta da casa" && rename.GetProperty("expectedRevision").GetInt32() == 1, "renaming is an update with the revision it was seen at");
            editor.Synchronizer.OnOperation(Op("update", StateOf(id, "Porta da casa", 2, Guid.Empty, last: Bruno)));
            Require(tab.NameForTesting == "Porta da casa", "the answer of the server is what stays");
            tab.NameForTesting = "   "; commands.Clear(); tab.CommitNameForTesting();
            Require(commands.Count == 0 && tab.NameForTesting == "Porta da casa", "an empty name is refused before it is sent");

            // click actions
            commands.Clear(); tab.ChooseClickForTesting("sit");
            Require(Last(commands).GetProperty("operation").GetString() == "properties" && Last(commands).GetProperty("properties").GetProperty("clickAction").GetString() == "sit", "choosing an action sends it");
            editor.Synchronizer.OnOperation(Props(StateOf(id, "Porta da casa", 3, Guid.Empty, click: "sit", last: Bruno)));
            Require(tab.ClickForTesting == "sit", "the action chosen is the one shown");
            commands.Clear(); tab.ChooseClickForTesting("buy");
            Require(commands.Count == 0 && editor.Window.StatusForTesting.Contains("à venda") && tab.ClickForTesting == "sit", "Comprar needs a sale first and the tab says so");

            // sale
            tab.ToggleForSaleForTesting(true); Require(tab.SaleRowsVisibleForTesting, "the sale options appear");
            tab.SetSaleForTesting("copy", 250); commands.Clear(); tab.PressApplyForTesting();
            var sale = Last(commands).GetProperty("properties");
            Require(sale.GetProperty("saleType").GetString() == "copy" && sale.GetProperty("salePrice").GetInt32() == 250, "Aplicar puts it on sale with the mode and the price");
            editor.Synchronizer.OnOperation(Props(StateOf(id, "Porta da casa", 4, Guid.Empty, "{\"saleType\":\"copy\",\"salePrice\":250}", "sit", Bruno)));
            Require(tab.ForSaleForTesting && tab.SaleRowsVisibleForTesting, "after the answer it shows as for sale");
            commands.Clear(); tab.ChooseClickForTesting("buy");
            Require(Last(commands).GetProperty("properties").GetProperty("clickAction").GetString() == "buy", "now Comprar can be chosen");
            editor.Synchronizer.OnOperation(Props(StateOf(id, "Porta da casa", 5, Guid.Empty, "{\"saleType\":\"copy\",\"salePrice\":250}", "buy", Bruno)));
            commands.Clear(); tab.ToggleForSaleForTesting(false);
            Require(Last(commands).GetProperty("properties").GetProperty("saleType").GetString() == "none", "unchecking takes it off sale");
            Require(BuildObjectProperties.SaleProblem("original", 10_000_001) is not null && BuildObjectProperties.SaleProblem("nada", 5) is not null && BuildObjectProperties.SaleProblem("original", 0) is null, "price and mode are checked before sending");

            // permissions
            editor.Synchronizer.OnOperation(Props(StateOf(id, "Porta da casa", 6, Guid.Empty, "{}", "touch", Bruno)));
            commands.Clear(); tab.ToggleAnyoneMoveForTesting(true);
            Require(Last(commands).GetProperty("properties").GetProperty("anyoneMove").GetBoolean(), "anyone may move");
            commands.Clear(); tab.ToggleNextForTesting(true, true, false);
            var next = Last(commands).GetProperty("properties").GetProperty("nextOwnerRights").GetInt32();
            Require(next == (ItemRights.Modify | ItemRights.Copy), $"the next owner rights are the ones checked ({next})");

            // an object that is not mine: everything is read-only
            editor.Synchronizer.OnOperation(Op("create", StateOf(other, "Sofá", 1, Carla, x: 4)));
            editor.Selection.Set([other]); await Frames(2);
            Require(!tab.EditableForTesting && tab.OwnerForTesting == "Carla" && tab.NoteForTesting.Contains("não é seu"), "somebody else's object can only be looked at");
            commands.Clear(); tab.NameForTesting = "Roubado"; tab.CommitNameForTesting();
            Require(commands.Count == 0, "nothing is sent for an object that is not mine");

            // two objects at once
            editor.Synchronizer.OnOperation(Op("create", StateOf(Guid.NewGuid(), "Outra", 1, Guid.Empty, x: -4)));
            editor.Selection.Set(editor.Synchronizer.Views.Keys.Where(k => k != other)); await Frames(2);
            Require(editor.Selection.Count == 2 && tab.NameForTesting == "" && tab.NoteForTesting.Contains("2 objetos"), "with several objects the names are mixed and left alone");

            // buying
            editor.Selection.Set([id]); commands.Clear();
            Require(editor.Synchronizer.Buy(id, 250, "compra-teste-01") && Last(commands).GetProperty("operation").GetString() == "buy" && Last(commands).GetProperty("properties").GetProperty("expectedPrice").GetInt64() == 250, "a purchase carries the price the buyer saw and a key");
            editor.Synchronizer.OnOperation(JsonSerializer.Serialize(new { operation = "bought", objectId = id, saleType = "original", price = 250, buyerUserId = Guid.Empty, delivered = new[] { "Porta" }, objects = new[] { StateOf(id, "Porta da casa", 7, Guid.Empty, "{}", "touch", Carla) } }));
            Require(editor.Window.StatusForTesting.Contains("agora é seu") && editor.Synchronizer.Views[id].State.LastOwnerUserId == Carla, "the buyer is told and the previous owner is recorded");
            editor.Synchronizer.RejectForTesting("insufficient_balance");
            Require(editor.Window.StatusForTesting.Contains("Saldo insuficiente"), "a refused purchase says why");
            GD.Print("event=build_general_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=build_general_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
