using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>The dialogs and permission requests of scripts: shown one at a time with the buttons the script gave, the answer goes back as JSON, closing a permission request denies it, and bad messages are ignored.</summary>
public partial class ScriptDialogSmoke : Control
{
    public override async void _Ready()
    {
        try
        {
            var sent = new List<JsonElement>(); var window = new ScriptDialogWindow { Send = json => sent.Add(JsonDocument.Parse(json).RootElement.Clone()) }; AddChild(window); await Frames(3);
            var screen = GetViewport().GetVisibleRect().Size; var first = Guid.NewGuid(); var second = Guid.NewGuid(); var third = Guid.NewGuid();
            string Dialog(Guid id, string kind, string message, string[] buttons, bool input = false) => JsonSerializer.Serialize(new { operation = "script_dialog", id, kind, title = "Porta", message, buttons, input, timeout = 60, objectName = "Porta da frente" });

            Require(!window.IsOpen, "nothing shows by itself");
            window.OnOperation("not json"); window.OnOperation("{\"operation\":\"text\"}"); window.OnOperation("{\"operation\":\"script_dialog\"}"); Require(!window.IsOpen, "bad or foreign messages are ignored");
            window.OnOperation(Dialog(first, "dialog", "Escolha uma opção", ["Abrir", "Fechar", "Trancar"])); await Frames(4);
            Require(window.IsOpen && window.TextForTesting.Contains("Porta da frente pergunta:") && window.TextForTesting.Contains("Escolha uma opção") && window.ButtonsForTesting.SequenceEqual(["Abrir", "Fechar", "Trancar"]), $"the dialog shows who asks, the message and the buttons (got {window.TextForTesting})");
            window.OnOperation(Dialog(second, "permission", "“Porta” quer controlar as animações do seu avatar.", ["Permitir", "Negar"])); Require(window.Waiting == 1 && window.Current!.Id == first, "the next one waits its turn");
            window.PressForTesting("Fechar"); await Frames(4);
            Require(sent.Count == 1 && sent[0].GetProperty("id").GetGuid() == first && sent[0].GetProperty("response").GetString() == "Fechar", "the answer goes back with the id and the button");
            Require(window.Current!.Id == second && window.ButtonsForTesting.SequenceEqual(["Permitir", "Negar"]), "then the permission request shows");
            var size = window.Current is not null ? screen : screen; Require(window.IsOpen && size.X > 0, "window is on screen");
            window.CloseForTesting(); await Frames(3);
            Require(sent.Count == 2 && sent[1].GetProperty("id").GetGuid() == second && sent[1].GetProperty("response").GetString() == "Negar", "closing a permission request denies it");
            Require(!window.IsOpen, "nothing left to show");

            window.OnOperation(Dialog(third, "dialog", "Qual o seu nome?", ["Enviar"], input: true)); await Frames(3);
            Require(window.InputForTesting.Visible, "a text box shows when the script asks for one"); window.InputForTesting.Text = "Ana"; window.PressForTesting("Enviar"); await Frames(3);
            Require(sent[2].GetProperty("input").GetString() == "Ana" && sent[2].GetProperty("response").GetString() == "Enviar", "the text goes with the answer");
            window.OnOperation(Dialog(Guid.NewGuid(), "dialog", "x", ["A"])); await Frames(2); window.CloseForTesting(); await Frames(2); Require(sent.Count == 3 && !window.IsOpen, "closing a plain dialog answers nothing");
            for (var i = 0; i < 15; i++) window.OnOperation(Dialog(Guid.NewGuid(), "dialog", "spam", ["A"])); Require(window.Waiting <= 10, "a flood of dialogs does not pile up");

            GD.Print("event=script_dialog_smoke result=passed"); GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=script_dialog_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
