using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>The HUD panels of scripts: drawn from what the island sends, updated without losing what the player typed, clicks, texts and closing sent back, removed when the island says so, bad messages ignored and the number of panels limited.</summary>
public partial class ScriptHudSmoke : Control
{
    public override async void _Ready()
    {
        try
        {
            var sent = new List<JsonElement>(); var layer = new ScriptHudLayer { Send = json => sent.Add(JsonDocument.Parse(json).RootElement.Clone()) }; AddChild(layer); await Frames(3);
            var screen = GetViewport().GetVisibleRect().Size; var script = Guid.NewGuid();
            string Hud(Guid id, bool visible, string title, object[] elements, float x = .8f, float y = .25f, int width = 260) => JsonSerializer.Serialize(new { operation = "script_hud", script = id, objectName = "Loja", visible, title, x, y, width, elements });
            object[] Panel(string status = "Vida: 100", double energy = 70) => [new { id = "status", type = "label", text = status, color = "#3BF45F", value = 0, max = 0, placeholder = "" }, new { id = "ativar", type = "button", text = "Ativar", color = "", value = 0, max = 0, placeholder = "" },
                new { id = "energia", type = "progress", text = "Energia", color = "", value = energy, max = 100, placeholder = "" }, new { id = "nome", type = "input", text = "", color = "", value = 0, max = 0, placeholder = "Seu nome" }];

            layer.OnOperation("not json"); layer.OnOperation("{\"operation\":\"text\"}"); layer.OnOperation("{\"operation\":\"script_hud\"}"); Require(layer.PanelCount == 0, "bad or foreign messages draw nothing");
            layer.OnOperation(Hud(script, true, "Painel da loja", Panel())); await Frames(6);
            Require(layer.Has(script) && layer.PanelCount == 1 && layer.TextOf(script).Contains("Painel da loja") && layer.TextOf(script).Contains("Loja") && layer.TextOf(script).Contains("Vida: 100") && layer.TextOf(script).Contains("Energia"), $"title, object and texts are drawn (got {layer.TextOf(script)})");
            Require(layer.ButtonsOf(script).Contains("Ativar") && layer.BarOf(script) is { Value: 70, MaxValue: 100 } && layer.InputOf(script) is { PlaceholderText: "Seu nome", MaxLength: 200 }, "a button, a progress bar and a text field");
            var box = layer.BoxOf(script)!; var rect = box.GetGlobalRect(); Require(rect.Position.X >= 0 && rect.End.X <= screen.X + 1 && rect.End.Y <= screen.Y + 1 && Mathf.Abs(rect.GetCenter().X - screen.X * .8f) < 8, $"the panel sits where the script put it and fits the screen (rect {rect}, screen {screen})");

            layer.PressForTesting(script, "Ativar"); Require(sent.Count == 1 && sent[0].GetProperty("hud").GetBoolean() && sent[0].GetProperty("script").GetGuid() == script && sent[0].GetProperty("action").GetString() == "click" && sent[0].GetProperty("element").GetString() == "ativar", "a click goes back with the script and the element");
            var field = layer.InputOf(script)!; field.Text = "Ana"; layer.OnOperation(Hud(script, true, "Painel da loja", Panel("Vida: 90", 40))); await Frames(6);
            Require(layer.TextOf(script).Contains("Vida: 90") && layer.BarOf(script)!.Value == 40 && layer.InputOf(script)!.Text == "Ana" && layer.PanelCount == 1, "an update changes the panel without losing what was typed");
            layer.InputOf(script)!.EmitSignal(LineEdit.SignalName.TextSubmitted, "Ana"); Require(sent[1].GetProperty("action").GetString() == "input" && sent[1].GetProperty("element").GetString() == "nome" && sent[1].GetProperty("text").GetString() == "Ana" && layer.InputOf(script)!.Text == "", "Enter sends the text and empties the field");

            layer.OnOperation(Hud(Guid.NewGuid(), true, "Outro", [new { id = "x", type = "label", text = new string('y', 500), color = "not-a-color", value = 0, max = 0, placeholder = "" }], width: 9999, x: -4, y: 9)); await Frames(4);
            Require(layer.PanelCount == 2, "another script has its own panel"); var wide = layer.BoxOf(script)!.Size.X; Require(wide <= 421, $"a width beyond the limit is cut (got {wide})");
            for (var i = 0; i < 12; i++) layer.OnOperation(Hud(Guid.NewGuid(), true, $"P{i}", Panel())); Require(layer.PanelCount == ScriptHudLayer.MaxPanels, $"at most {ScriptHudLayer.MaxPanels} panels (got {layer.PanelCount})");

            layer.CloseForTesting(script); await Frames(3); Require(sent[^1].GetProperty("action").GetString() == "close" && sent[^1].GetProperty("script").GetGuid() == script && !layer.Has(script), "the player can always close a panel, and the island is told");
            layer.OnOperation(Hud(script, true, "Volta", Panel())); await Frames(3); Require(layer.Has(script), "a script may show it again"); layer.OnOperation(Hud(script, false, "", [])); await Frames(3); Require(!layer.Has(script), "the island can take a panel down");
            layer.Clear(); Require(layer.PanelCount == 0, "leaving the island clears every panel");

            GD.Print("event=script_hud_smoke result=passed"); GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=script_hud_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
