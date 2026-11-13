using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Groups;

namespace LifeVerse.Client.Tests;

/// <summary>The creation wizard (four steps, checks, the body sent) and "Descobrir Grupos" (search, filters, opening a result) against a pretend server.</summary>
public partial class GroupCreateSmoke : Control
{
    public override async void _Ready()
    {
        try
        {
            var calls = new List<string>(); var bodies = new List<string>(); var created = Guid.NewGuid(); var nameTaken = true; Guid? createdSeen = null; Guid? opened = null; var said = new List<string>();
            Task<(long, string)> Server(string method, string path, string? body)
            {
                calls.Add($"{method} {path}"); if (body is not null) bodies.Add(body);
                if (method == "POST" && path == "/api/v1/groups")
                {
                    if (nameTaken) { nameTaken = false; return Task.FromResult<(long, string)>((409, "{\"error\":\"name_taken\"}")); }
                    return Task.FromResult<(long, string)>((201, $"{{\"id\":\"{created}\"}}"));
                }
                if (path.StartsWith("/api/v1/groups/search", StringComparison.Ordinal))
                {
                    var free = path.Contains("free=true", StringComparison.Ordinal); var category = path.Contains("category=musica", StringComparison.Ordinal);
                    var all = new[] { (Guid.NewGuid(), "Banda LV", "BLV", "musica", "open", 0L), (Guid.NewGuid(), "Coral Paga", "CP", "musica", "open", 250L), (Guid.NewGuid(), "Pedreiros", "PED", "construcao", "request", 0L) };
                    var shown = all.Where(g => (!free || g.Item6 == 0) && (!category || g.Item4 == "musica"));
                    return Task.FromResult<(long, string)>((200, "[" + string.Join(",", shown.Select(g => $"{{\"id\":\"{g.Item1}\",\"name\":\"{g.Item2}\",\"tag\":\"{g.Item3}\",\"description\":\"Quem toca junto\",\"members\":7,\"category\":\"{g.Item4}\",\"joinPolicy\":\"{g.Item5}\",\"joinFee\":{g.Item6},\"isMember\":false}}")) + "]"));
                }
                return Task.FromResult<(long, string)>((404, "{}"));
            }

            // ---- the wizard ----
            var wizard = new GroupCreateWindow { Name = "Create", Created = id => createdSeen = id, Said = text => said.Add(text) }; wizard.SetTransportForTesting(Server); AddChild(wizard); await Frames(3);
            var screen = GetViewport().GetVisibleRect().Size;
            wizard.Open(); await Frames(6);
            Require(wizard.Visible && wizard.StepForTesting == 0 && wizard.TextForTesting.Contains("Nome do grupo") && !wizard.HasButtonForTesting("Voltar"), "starts on Informações without Voltar");
            wizard.PressForTesting("Próximo"); await Frames(3); Require(wizard.StepForTesting == 0 && wizard.NoteForTesting.Contains("nome"), "an empty name stops the first step");
            Node<LineEdit>(wizard, "name").Text = "Banda do Mirante"; Node<LineEdit>(wizard, "name").EmitSignal(LineEdit.SignalName.TextChanged, "Banda do Mirante");
            Node<LineEdit>(wizard, "tag").Text = "b!"; Node<LineEdit>(wizard, "tag").EmitSignal(LineEdit.SignalName.TextChanged, "b!");
            wizard.PressForTesting("Próximo"); await Frames(3); Require(wizard.StepForTesting == 0 && wizard.NoteForTesting.Contains("sigla"), "a bad sigla stops the first step");
            Node<LineEdit>(wizard, "tag").Text = "BDM"; Node<LineEdit>(wizard, "tag").EmitSignal(LineEdit.SignalName.TextChanged, "BDM");
            var category = Node<OptionButton>(wizard, "category"); category.Select(5); category.EmitSignal(OptionButton.SignalName.ItemSelected, 5L);
            wizard.PressForTesting("Próximo"); await Frames(6); Require(wizard.StepForTesting == 1 && wizard.HasButtonForTesting("Voltar") && wizard.TextForTesting.Contains("Como se entra"), "step 2 is Entrada");

            var policy = Node<OptionButton>(wizard, "policy"); var fee = Node<SpinBox>(wizard, "fee");
            Require(!fee.GetParent<Control>().Visible, "the fee is hidden unless the entry is free"); policy.Select(0); policy.EmitSignal(OptionButton.SignalName.ItemSelected, 0L); Require(fee.GetParent<Control>().Visible, "an open group can ask a fee"); fee.Value = 75;
            var privacy = Node<OptionButton>(wizard, "privacy"); privacy.Select(1); privacy.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
            Require(!fee.GetParent<Control>().Visible && policy.Disabled && IslandUiId(policy) == "invite", "a private group is by invitation only and has no fee");
            privacy.Select(0); privacy.EmitSignal(OptionButton.SignalName.ItemSelected, 0L); policy.Select(0); policy.EmitSignal(OptionButton.SignalName.ItemSelected, 0L); fee.Value = 75;
            wizard.PressForTesting("Voltar"); await Frames(6); Require(wizard.StepForTesting == 0 && Node<LineEdit>(wizard, "name").Text == "Banda do Mirante", "going back keeps what was typed");
            wizard.PressForTesting("Próximo"); await Frames(6); wizard.PressForTesting("Próximo"); await Frames(6); Require(wizard.StepForTesting == 2 && wizard.TextForTesting.Contains("Classificação de conteúdo"), "step 3 is Configurações");
            var cover = Node<LineEdit>(wizard, "cover"); cover.Text = "http://ruim"; cover.EmitSignal(LineEdit.SignalName.TextChanged, "http://ruim"); wizard.PressForTesting("Próximo"); await Frames(3); Require(wizard.StepForTesting == 2 && wizard.NoteForTesting.Contains("https"), "a cover must be https");
            cover.Text = ""; cover.EmitSignal(LineEdit.SignalName.TextChanged, ""); wizard.PressForTesting("Próximo"); await Frames(6);
            Require(wizard.StepForTesting == 3 && wizard.HasButtonForTesting("Criar grupo") && wizard.TextForTesting.Contains("Banda do Mirante  [BDM]") && wizard.TextForTesting.Contains("taxa L$ 75") && wizard.TextForTesting.Contains("Criar um grupo é gratuito"), $"the confirmation summarises everything (got {wizard.TextForTesting})");
            var size = wizard.SizeForTesting; Require(size.X <= screen.X && size.Y <= screen.Y, $"the wizard fits the screen (size {size}, screen {screen})");
            wizard.PressForTesting("Criar grupo"); await Frames(12); Require(wizard.Visible && wizard.NoteForTesting.Contains("Já existe"), $"a server refusal is shown in words and the wizard stays (got {wizard.NoteForTesting})");
            wizard.PressForTesting("Criar grupo"); await Frames(12);
            var sent = JsonNode.Parse(bodies.Last(b => b.Contains("Banda do Mirante")))!;
            Require(!wizard.Visible && createdSeen == created && said.Last().Contains("Banda do Mirante"), "creating closes the wizard and reports the new group");
            Require(sent["name"]!.GetValue<string>() == "Banda do Mirante" && sent["tag"]!.GetValue<string>() == "BDM" && sent["category"]!.GetValue<string>() == "musica" && sent["joinPolicy"]!.GetValue<string>() == "open" && sent["joinFee"]!.GetValue<long>() == 75 && sent["privacy"]!.GetValue<string>() == "public", $"the body (got {sent})");

            // ---- discover ----
            var discover = new GroupDiscoverWindow { Name = "Discover", OpenGroup = id => opened = id }; discover.SetTransportForTesting(Server); AddChild(discover); await Frames(3);
            await discover.OpenAsync(); await Frames(6); var text = discover.TextForTesting;
            Require(text.Contains("Banda LV  [BLV]") && text.Contains("Coral Paga") && text.Contains("Pedreiros") && text.Contains("Entrada livre · grátis") && text.Contains("Entrada livre · L$ 250") && text.Contains("Por aprovação") && discover.NoteForTesting == "3 grupo(s)", $"all the groups (got {text})");
            var s = discover.SizeForTesting; Require(s.X <= screen.X && s.Y <= screen.Y, "the discover window fits the screen");
            discover.FilterForTesting("coral", "musica", "", "", true); discover.PressForTesting("Buscar"); await Frames(10); text = discover.TextForTesting;
            Require(text.Contains("Banda LV") && !text.Contains("Coral Paga") && !text.Contains("Pedreiros") && calls.Last().Contains("q=coral") && calls.Last().Contains("category=musica") && calls.Last().Contains("free=true"), $"filters narrow the search and are sent (last call {calls.Last()}, got {text})");
            discover.PressForTesting("Ver grupo"); Require(opened is not null, "a result opens the group profile");

            GD.Print("event=group_create_smoke result=passed"); GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=group_create_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private static T Node<T>(GroupCreateWindow w, string name) where T : Node => (T)w.BodyForTesting.FindChild(name, true, false);
    private static string IslandUiId(OptionButton drop) => drop.Selected >= 0 ? drop.GetItemMetadata(drop.Selected).AsString() : "";
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
