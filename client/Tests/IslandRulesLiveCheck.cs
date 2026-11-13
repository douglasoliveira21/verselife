using System.Net.Http;
using System.Text;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.IslandManagement;

namespace LifeVerse.Client.Tests;

/// <summary>
/// The island Property tab against a REAL API (not a pretend one): reads the island, saves the island rules through the same page code the window uses and checks what the page and the server say afterwards.
/// Needs LIFEVERSE_API_URL, LIFEVERSE_TEST_TOKEN (the access token of the island owner) and LIFEVERSE_TEST_ISLAND; without them it only says it was skipped. Meant for a local or test API, never for production data.
/// </summary>
public partial class IslandRulesLiveCheck : Node3D
{
    public override async void _Ready()
    {
        try
        {
            string Env(string name) => OS.GetEnvironment(name);
            if (Env("LIFEVERSE_API_URL").Length == 0 || Env("LIFEVERSE_TEST_TOKEN").Length == 0 || !Guid.TryParse(Env("LIFEVERSE_TEST_ISLAND"), out var island)) { GD.Print("event=island_rules_live result=skipped reason=no_environment"); GetTree().Quit(); return; }
            var http = new System.Net.Http.HttpClient { BaseAddress = new Uri(Env("LIFEVERSE_API_URL")), Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Env("LIFEVERSE_TEST_TOKEN"));
            var calls = new List<string>();
            Task<(long, string)> Transport(string method, string path, string? body)
                => Task.Run(async () =>
                {
                    var request = new HttpRequestMessage(new System.Net.Http.HttpMethod(method), path) { Content = body is null ? null : new StringContent(body, Encoding.UTF8, "application/json") };
                    using var response = await http.SendAsync(request); var text = await response.Content.ReadAsStringAsync(); calls.Add($"{method} {path} -> {(int)response.StatusCode} {(body is null ? "" : body)} => {text[..Math.Min(text.Length, 160)]}");
                    return ((long)(int)response.StatusCode, text);
                });
            var notes = new List<string>();
            var page = new IslandPropertyPage { Name = "Property", Theme = ViewerTheme.Create() }; page.Notify += text => notes.Add(text); page.Bind(new IslandManagementApi(Transport)); page.SetIsland(island); AddChild(page);
            await Frames(3); await page.LoadAsync(); await Frames(3);
            GD.Print($"before: {page.RulesLineForTesting}");
            // the way a player does it: open the dialog, tick the box, press Salvar
            page.OpenRulesForTesting(); await Frames(8); var dialog = page.DialogsForTesting.Last();
            var boxes = dialog.FindChildren("*", "CheckBox", true, false).OfType<CheckBox>().ToList(); GD.Print("dialog boxes: " + string.Join(" | ", boxes.Select(b => $"{b.Text}={b.ButtonPressed}")));
            boxes.First(b => b.Text.Contains("scripts")).ButtonPressed = true; dialog.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed); await Frames(30);
            GD.Print($"after dialog (page): {page.RulesLineForTesting} notes=[{string.Join(" | ", notes)}]");
            var ok = await page.SetRulesForTesting("public", "owner", true, true, true, null, false); await Frames(3);
            GD.Print($"saved ok={ok} notes=[{string.Join(" | ", notes)}]");
            GD.Print($"after (page): {page.RulesLineForTesting}");
            foreach (var line in calls) GD.Print("call: " + line);
            GD.Print("event=island_rules_live result=done");
        }
        catch (Exception exception) { GD.PushError($"event=island_rules_live result=failed error={exception}"); GetTree().Quit(1); return; }
        GetTree().Quit();
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
}
