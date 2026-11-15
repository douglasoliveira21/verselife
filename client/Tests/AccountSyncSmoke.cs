using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts.Settings;
using LifeVerse.Shared.Settings;

namespace LifeVerse.Client.Tests;

/// <summary>Stage 5 of the settings menu: the account sync against a stand-in server that applies the real shared rules. What is sent, what is never sent, conflicts, offline and signed-out behaviour, and that the client catalog agrees with the server's rules.</summary>
public partial class AccountSyncSmoke : Node
{
    private const string Root = "user://smoke_account_sync";
    private static void WipeDir(string path) { var absolute = ProjectSettings.GlobalizePath(path); if (!DirAccess.DirExistsAbsolute(absolute)) return; foreach (var file in DirAccess.GetFilesAt(absolute)) DirAccess.RemoveAbsolute($"{absolute}/{file}"); }

    private sealed class FakeServer
    {
        public int Revision; public Dictionary<string, object?> Values = []; public bool Offline; public int Calls; public List<string> LastKeys = [];
        public void Send(string method, string path, string? body, Action<int, string> done)
        {
            Calls++;
            if (Offline) { done(0, ""); return; }
            if (method == "GET") { done(200, Json()); return; }
            var patch = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonDocument.Parse(body!).RootElement.GetProperty("values").GetRawText())!;
            var baseRevision = JsonDocument.Parse(body!).RootElement.GetProperty("baseRevision").GetInt32();
            var check = AccountPreferenceRules.Check(patch);
            if (!check.Ok) { done(400, "{}"); return; }
            if (baseRevision != Revision) { done(409, Json()); return; }
            LastKeys = [.. patch.Keys];
            foreach (var (id, value) in check.Values) { if (value is null) Values.Remove(id); else Values[id] = value; }
            Revision++; done(200, Json());
        }
        private string Json() => JsonSerializer.Serialize(new { revision = Revision, values = Values });
    }

    public override async void _Ready()
    {
        try
        {
            WipeDir(Root); DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(Root + "_conta.json"));
            // ---- the client catalog agrees with the rules the server enforces
            foreach (var rule in AccountPreferenceRules.All)
            {
                var d = SettingsCatalog.All.FirstOrDefault(x => x.Id == rule.Id);
                Require(d is not null && d.IsAvailable && d.IsValueSetting, $"{rule.Id}: exists in the client as a real setting");
                var kindOk = rule.Kind switch { PreferenceKind.Bool => d!.Kind == SettingKind.Bool, PreferenceKind.Int => d!.Kind == SettingKind.Int, PreferenceKind.Float => d!.Kind == SettingKind.Float, PreferenceKind.Choice => d!.Kind == SettingKind.Choice, _ => d!.Kind == SettingKind.Key };
                Require(kindOk, $"{rule.Id}: same kind on both sides ({d!.Kind} vs {rule.Kind})");
                if (rule.Kind is PreferenceKind.Int or PreferenceKind.Float) Require(d.Min >= rule.Min && d.Max <= rule.Max, $"{rule.Id}: the client range {d.Min}..{d.Max} fits the server's {rule.Min}..{rule.Max}");
                if (rule.Kind == PreferenceKind.Choice) Require(d.CurrentChoices.All(c => rule.Choices!.Contains(c.Id)), $"{rule.Id}: every client choice is accepted by the server");
                var defaultJson = JsonSerializer.SerializeToElement(d.Default);
                Require(AccountPreferenceRules.CheckValue(rule, defaultJson).Error is null, $"{rule.Id}: the default is accepted by the server");
            }
            foreach (var d in SettingsCatalog.All.Where(d => d.Id.StartsWith("graphics.") || d.Id.StartsWith("display.") || d.Id.StartsWith("perf.") || d.Id.StartsWith("metrics.") || d.Id.StartsWith("audio.input") || d.Id.StartsWith("audio.output")))
                Require(!AccountSync.IsSynced(d.Id), $"{d.Id}: stays on this computer");

            // ---- turning it on with an empty account sends this device's values (and only the shared ones)
            var store = new SettingsStore(Root, SettingsCatalog.All, SettingsCatalog.Presets);
            var server = new FakeServer(); var user = Guid.NewGuid();
            var sync = new AccountSync(store, Root + "_conta.json", server.Send, () => user);
            Require(sync.SignedIn && !sync.Enabled, "signed in, sync off by default");
            store.Set("chat.font_size", 20); store.Apply();
            var calls = server.Calls; sync.PushApplied(["chat.font_size"], _ => { });
            Require(server.Calls == calls, "nothing is sent while sync is off");

            AccountSnapshot? snap = null; sync.Fetch(s => snap = s);
            Require(snap is { Result: SyncResult.Ok, Revision: 0 } && snap.Values.Count == 0, "an empty account answers revision 0");
            sync.PushAll(snap!.Revision, s => snap = s);
            Require(snap.Result == SyncResult.Ok && sync.Enabled && sync.Revision == 1 && server.Revision == 1, "the first push turns sync on at revision 1");
            Require(server.Values.Count == sync.SyncedIds.Count() && server.Values.Keys.All(AccountSync.IsSynced) && !server.Values.Keys.Any(k => k.StartsWith("graphics.") || k.StartsWith("display.")), "only shared settings reached the server");
            Require(Convert.ToInt64(server.Values["chat.font_size"]) == 20, "this device's values are what the account holds");

            // ---- a change on this device goes up on its own; device-only settings never do
            store.Set("chat.font_size", 16); store.Set("graphics.vsync", false); var result = store.Apply();
            sync.PushApplied(result.Changed, s => snap = s);
            Require(snap!.Result == SyncResult.Ok && server.Revision == 2 && server.LastKeys.SequenceEqual(["chat.font_size"]), "only the changed shared setting is sent");

            // ---- another device changes the account: our push is refused and the account's values come back
            server.Values["chat.font_size"] = 24L; server.Revision = 5;
            store.Set("chat.font_size", 14); sync.PushApplied(store.Apply().Changed, s => snap = s);
            Require(snap!.Result == SyncResult.Conflict && snap.Revision == 5 && Convert.ToInt32(snap.Values["chat.font_size"]) == 24 && server.Revision == 5, "a stale push is a conflict, the server is untouched");
            sync.UseAccount(snap); Require(store.Get<int>("chat.font_size") == 24 && sync.Revision == 5, "using the account's values applies them here");
            store.Set("chat.font_size", 13); store.Apply(); sync.PushAll(5, s => snap = s);
            Require(snap!.Result == SyncResult.Ok && server.Revision == 6 && Convert.ToInt64(server.Values["chat.font_size"]) == 13, "replacing the account with this device's values works after seeing its revision");

            // ---- offline: nothing is lost, the status says so, sync stays on
            server.Offline = true; store.Set("chat.font_size", 12); sync.PushApplied(store.Apply().Changed, s => snap = s);
            Require(snap!.Result == SyncResult.Offline && sync.Enabled && store.Get<int>("chat.font_size") == 12 && sync.Status.Contains("Sem conexão"), "offline keeps the local value and says so");
            server.Offline = false;

            // ---- pull when the world loads
            server.Values["chat.font_size"] = 18L; server.Revision = 9; sync.PullIfEnabled();
            Require(store.Get<int>("chat.font_size") == 18 && sync.Revision == 9, "pulling brings the account's newer values");

            // ---- the state remembers the revision but another account does not inherit it
            var again = new AccountSync(store, Root + "_conta.json", server.Send, () => user);
            Require(again.Enabled && again.Revision == 9, "the choice survives a restart");
            var someoneElse = new AccountSync(store, Root + "_conta.json", server.Send, () => Guid.NewGuid());
            Require(!someoneElse.Enabled, "another person on this computer starts with sync off");
            var signedOut = new AccountSync(store, Root + "_conta.json", server.Send, () => Guid.Empty);
            AccountSnapshot? out1 = null; signedOut.Fetch(s => out1 = s);
            Require(!signedOut.Enabled && out1!.Result == SyncResult.Unauthorized, "signed out: no sync, a clear answer");

            // ---- turning it off
            sync.TurnOff(); calls = server.Calls; sync.PushApplied(["chat.font_size"], _ => { });
            Require(!sync.Enabled && server.Calls == calls, "after turning it off nothing is sent");
            var text = Godot.FileAccess.GetFileAsString(Root + "_conta.json");
            Require(!text.Contains("token", StringComparison.OrdinalIgnoreCase) && text.Length < 200, "the state file holds no secrets");

            // ---- the window
            var window = new SettingsWindow(store) { Sync = new AccountSync(store, Root + "_conta2.json", server.Send, () => Guid.Empty) };
            AddChild(window); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(window.ScopeForTesting.IsItemDisabled(1), "signed out: the Conta scope is disabled");
            window.Sync = new AccountSync(store, Root + "_conta3.json", server.Send, () => user); window.Sync.Changed += () => { };
            window.QueueFree();
            var window2 = new SettingsWindow(store) { Sync = new AccountSync(store, Root + "_conta3.json", server.Send, () => user) };
            AddChild(window2); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!window2.ScopeForTesting.IsItemDisabled(1) && window2.ScopeForTesting.Selected == 0, "signed in: Conta is available and Local is selected");
            window2.ScopeForTesting.Select(1); window2.ScopeForTesting.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
            Require(window2.Dialog.IsOpen && window2.Dialog.MessageForTesting.Contains("Nenhuma senha"), "choosing Conta asks first and says what stays local");
            window2.Dialog.ButtonsForTesting[0].EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(window2.Dialog.IsOpen && window2.Dialog.ButtonsForTesting.Count == 3, "the account already holds values: the window asks which side wins");
            window2.Dialog.ButtonsForTesting[0].EmitSignal(BaseButton.SignalName.Pressed);
            Require(window2.Sync!.Enabled && window2.ScopeForTesting.Selected == 1, "using the account's values turns sync on");

            WipeDir(Root); foreach (var f in new[] { "_conta", "_conta2", "_conta3" }) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(Root + f + ".json"));
            GD.Print("event=account_sync_smoke result=passed");
            GetTree().Quit(0);
        }
        catch (Exception exception) { GD.PrintErr($"event=account_sync_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
