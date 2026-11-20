using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LifeVerse.Unity.Avatar;
using UnityEngine;

namespace LifeVerse.Unity.UI
{
    public enum SyncResult { Ok, Conflict, Offline, Unauthorized, Invalid }

    /// <summary>What the account holds: the revision and the values (each already checked against its rule).</summary>
    public sealed class AccountSnapshot { public SyncResult Result; public int Revision; public Dictionary<string, object> Values = new Dictionary<string, object>(); public string Message = ""; }

    /// <summary>
    /// Settings that follow the account to every device (the same list of ids and the same rules the platform checks, see <c>AccountPreferenceRules</c> in the shared code): fetch, send what changed, and tell the person when another device
    /// got there first. Only the ids of the shared list leave this computer; graphics, devices and performance limits never do. Of that list this client has the ones that mean the same thing here: inverting the mouse, always running,
    /// the chat's opacity and text size, and reducing motion. Sync is on per account: another person on this computer starts with it off.
    /// </summary>
    public sealed class AccountSync
    {
        public const string Route = "/api/v1/users/me/preferences";
        public static readonly string[] SettingIds = { "controls.invert_y", "controls.always_run", "chat.opacity", "chat.font_size", "accessibility.reduce_motion" };

        /// <summary>The actions of the game that have a key on the account under the names of the platform ("controls.jump.1" is the first key of "jump"). The second key of an action stays as the account has it.</summary>
        public static readonly (string Name, GameAction Action)[] KeyActions =
        {
            ("move_forward", GameAction.Forward), ("move_back", GameAction.Back), ("move_left", GameAction.Left), ("move_right", GameAction.Right), ("jump", GameAction.Jump), ("run", GameAction.Run), ("fly_toggle", GameAction.Fly), ("descend", GameAction.Descend),
            ("emote_wave", GameAction.Wave), ("emote_dance", GameAction.Dance), ("first_person", GameAction.FirstPerson), ("build_mode", GameAction.Build), ("terrain_mode", GameAction.Terrain), ("voice_talk", GameAction.Talk),
        };

        public static string KeyId(string name) => "controls." + name + ".1";
        public static readonly string[] SyncedIds = SettingIds.Concat(KeyActions.Select(k => KeyId(k.Name))).ToArray();

        private static readonly (UnityEngine.InputSystem.Key Key, string Name)[] SpecialKeys =
        {
            (UnityEngine.InputSystem.Key.Space, "Space"), (UnityEngine.InputSystem.Key.LeftShift, "Shift"), (UnityEngine.InputSystem.Key.LeftCtrl, "Ctrl"), (UnityEngine.InputSystem.Key.LeftAlt, "Alt"), (UnityEngine.InputSystem.Key.Tab, "Tab"),
            (UnityEngine.InputSystem.Key.UpArrow, "Up"), (UnityEngine.InputSystem.Key.DownArrow, "Down"), (UnityEngine.InputSystem.Key.LeftArrow, "Left"), (UnityEngine.InputSystem.Key.RightArrow, "Right"),
        };

        /// <summary>A key as the platform names it ("W", "Space", "Shift", "F5"), or null when this game has no such name for it.</summary>
        public static string PlatformName(UnityEngine.InputSystem.Key key)
        {
            foreach (var (k, n) in SpecialKeys) if (k == key) return n;
            if (key >= UnityEngine.InputSystem.Key.A && key <= UnityEngine.InputSystem.Key.Z) return key.ToString();
            if (key >= UnityEngine.InputSystem.Key.Digit1 && key <= UnityEngine.InputSystem.Key.Digit9) return ((int)key - (int)UnityEngine.InputSystem.Key.Digit1 + 1).ToString(); if (key == UnityEngine.InputSystem.Key.Digit0) return "0";
            if (key >= UnityEngine.InputSystem.Key.F1 && key <= UnityEngine.InputSystem.Key.F12 && key != UnityEngine.InputSystem.Key.F10) return key.ToString();
            return null;
        }

        /// <summary>The key of a name of the platform, or <c>Key.None</c> when it is not one this game knows.</summary>
        public static UnityEngine.InputSystem.Key KeyOf(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return UnityEngine.InputSystem.Key.None; name = name.Trim();
            foreach (var (k, n) in SpecialKeys) if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return k;
            for (var k = UnityEngine.InputSystem.Key.A; k <= UnityEngine.InputSystem.Key.Z; k++) if (string.Equals(k.ToString(), name, StringComparison.OrdinalIgnoreCase)) return k;
            if (name.Length == 1 && char.IsDigit(name[0])) return name[0] == '0' ? UnityEngine.InputSystem.Key.Digit0 : (UnityEngine.InputSystem.Key)((int)UnityEngine.InputSystem.Key.Digit1 + (name[0] - '1'));
            for (var k = UnityEngine.InputSystem.Key.F1; k <= UnityEngine.InputSystem.Key.F12; k++) if (k != UnityEngine.InputSystem.Key.F10 && string.Equals(k.ToString(), name, StringComparison.OrdinalIgnoreCase)) return k;
            return UnityEngine.InputSystem.Key.None;
        }

        private readonly UserSettings _s; private readonly Func<string, string, string, Task<(int Status, string Body)>> _transport; private readonly Func<Guid> _user; private readonly Action _save;
        public string Status { get; private set; } = "";
        public event Action Changed;

        public AccountSync(UserSettings settings, Func<string, string, string, Task<(int Status, string Body)>> transport, Func<Guid> user, Action save = null) { _s = settings; _transport = transport; _user = user; _save = save; }

        public bool SignedIn => _user() != Guid.Empty;
        public int Revision => _s.SyncRevision;
        /// <summary>Sync is on for the person signed in now.</summary>
        public bool Enabled => _s.SyncAccount && SignedIn && _s.SyncUser == _user().ToString("N");

        /// <summary>The value this device holds for an id, in the shape the account keeps (bool, whole number, text).</summary>
        public object Value(string id)
        {
            foreach (var (name, action) in KeyActions) if (id == KeyId(name)) return PlatformName(KeyBindings.Of(action));
            return SettingValue(id);
        }

        private object SettingValue(string id) => id switch
        {
            "controls.invert_y" => _s.InvertY, "controls.always_run" => _s.AlwaysRun, "chat.opacity" => (long)Mathf.RoundToInt(Mathf.Clamp(_s.ChatOpacity * 100f, 30f, 100f)), "chat.font_size" => (long)Mathf.Clamp(_s.ChatFontSize, 12, 28), "accessibility.reduce_motion" => _s.ReduceMotion, _ => null,
        };

        public void Apply(string id, object value)
        {
            foreach (var (name, action) in KeyActions) if (id == KeyId(name)) { var key = KeyOf(value as string); if (key != UnityEngine.InputSystem.Key.None && KeyBindings.Allowed(key)) { KeyBindings.Set(action, key, out _); _s.Keys = KeyBindings.Save(); } return; }
            switch (id)
            {
                case "controls.invert_y": _s.InvertY = (bool)value; break; case "controls.always_run": _s.AlwaysRun = (bool)value; break; case "chat.opacity": _s.ChatOpacity = Convert.ToInt64(value) / 100f; break;
                case "chat.font_size": _s.ChatFontSize = (int)Convert.ToInt64(value); break; case "accessibility.reduce_motion": _s.ReduceMotion = (bool)value; break;
            }
            _s.Clamped();
        }

        /// <summary>Checks one value from the account against the rule of its id (the same ranges the platform enforces).</summary>
        public static object Check(string id, JsonElement e)
        {
            switch (id)
            {
                case "controls.invert_y": case "controls.always_run": case "accessibility.reduce_motion": return e.ValueKind == JsonValueKind.True ? true : e.ValueKind == JsonValueKind.False ? false : (object)null;
                case "chat.opacity": return e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var o) && o >= 30 && o <= 100 ? o : (object)null;
                case "chat.font_size": return e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var f) && f >= 12 && f <= 28 ? f : (object)null;
                case var key when key.StartsWith("controls.") && key.EndsWith(".1"): return e.ValueKind == JsonValueKind.String && ValidKeyName(e.GetString() ?? "") ? e.GetString() : (object)null;
                default: return null;
            }
        }

        /// <summary>The platform's rule for a key name: letters, digits, spaces and a few signs, up to 24 characters; some keys are reserved.</summary>
        public static bool ValidKeyName(string text)
        {
            if (text == null || text.Length > 24) return false; foreach (var c in text) if (!(c < 128 && char.IsLetterOrDigit(c)) && " +-_/.,;[]'=`\\".IndexOf(c) < 0) return false;
            return !new[] { "Escape", "Enter", "Kp Enter", "F10", "Delete" }.Any(r => string.Equals(r, text, StringComparison.OrdinalIgnoreCase));
        }

        private static SyncResult Classify(int status) => status == 200 ? SyncResult.Ok : status == 409 ? SyncResult.Conflict : status == 401 || status == 403 ? SyncResult.Unauthorized : status == 400 ? SyncResult.Invalid : SyncResult.Offline;

        public static string Describe(SyncResult result) => result switch
        {
            SyncResult.Unauthorized => "Entre na conta para sincronizar.", SyncResult.Offline => "Sem conexão com o servidor; as alterações ficam só neste computador por enquanto.", SyncResult.Invalid => "O servidor recusou algumas configurações.", SyncResult.Conflict => "A conta foi alterada em outro computador.", _ => "",
        };

        public static AccountSnapshot Parse(int status, string text)
        {
            var snapshot = new AccountSnapshot { Result = Classify(status) };
            if (snapshot.Result == SyncResult.Ok || snapshot.Result == SyncResult.Conflict)
            {
                try
                {
                    using var d = JsonDocument.Parse(text); var root = d.RootElement; if (root.TryGetProperty("revision", out var r) && r.TryGetInt32(out var n)) snapshot.Revision = n;
                    if (root.TryGetProperty("values", out var v) && v.ValueKind == JsonValueKind.Object) foreach (var p in v.EnumerateObject()) if (SyncedIds.Contains(p.Name) && Check(p.Name, p.Value) is { } value) snapshot.Values[p.Name] = value;
                }
                catch (JsonException) { snapshot.Result = SyncResult.Offline; }
            }
            snapshot.Message = Describe(snapshot.Result); return snapshot;
        }

        private void Remember(bool on, int revision, string status) { _s.SyncAccount = on; _s.SyncRevision = revision; _s.SyncUser = _user().ToString("N"); Status = status; _save?.Invoke(); Changed?.Invoke(); }
        public void TurnOff() => Remember(false, Revision, "Local: as configurações valem só neste computador.");

        /// <summary>Reads what the account holds. Nothing is applied here.</summary>
        public async Task<AccountSnapshot> FetchAsync()
        {
            if (!SignedIn) return new AccountSnapshot { Result = SyncResult.Unauthorized, Message = Describe(SyncResult.Unauthorized) };
            var (status, body) = await _transport("GET", Route, null); return Parse(status, body);
        }

        /// <summary>Applies what the account holds to this computer and starts following it.</summary>
        public int UseAccount(AccountSnapshot snapshot)
        {
            var count = 0; foreach (var pair in snapshot.Values) { Apply(pair.Key, pair.Value); count++; } Remember(true, snapshot.Revision, "Conta: " + count + " preferência(s) vieram da conta."); return count;
        }

        /// <summary>Sends these ids (only the ones of the shared list) with the revision this computer last saw. A conflict means another computer got there first.</summary>
        public async Task<AccountSnapshot> PushAsync(IEnumerable<string> ids)
        {
            var values = new Dictionary<string, object>(); foreach (var id in ids.Distinct()) if (SyncedIds.Contains(id) && Value(id) is { } sent) values[id] = sent;   // a key this game cannot name is not sent
            if (values.Count == 0) return new AccountSnapshot { Result = SyncResult.Ok, Revision = Revision };
            if (!SignedIn) return new AccountSnapshot { Result = SyncResult.Unauthorized, Values = values, Message = Describe(SyncResult.Unauthorized) };
            var (status, text) = await _transport("PUT", Route, JsonSerializer.Serialize(new { baseRevision = Revision, values }, new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() }));
            var answer = Parse(status, text);
            if (answer.Result == SyncResult.Ok) Remember(true, answer.Revision, "Conta: sincronizado (revisão " + answer.Revision + ").");
            else { Status = answer.Message.Length > 0 ? "Conta: " + answer.Message : Status; Changed?.Invoke(); }
            return answer;
        }

        /// <summary>Turns sync on with this computer's values (replacing the account's).</summary>
        public Task<AccountSnapshot> PushAllAsync(int baseRevision) { _s.SyncRevision = baseRevision; return PushAsync(SyncedIds); }

        /// <summary>After a change of settings: sends the shared ones when sync is on.</summary>
        public Task<AccountSnapshot> PushAppliedAsync() => Enabled ? PushAsync(SyncedIds) : Task.FromResult(new AccountSnapshot { Result = SyncResult.Ok, Revision = Revision });

        /// <summary>At sign-in: fetches and applies what the account holds, when sync is on and another computer changed it.</summary>
        public async Task<AccountSnapshot> PullIfEnabledAsync()
        {
            if (!Enabled) return new AccountSnapshot { Result = SyncResult.Ok, Revision = Revision };
            var snapshot = await FetchAsync(); if (snapshot.Result == SyncResult.Ok && snapshot.Revision != Revision) UseAccount(snapshot); else if (snapshot.Result != SyncResult.Ok) { Status = "Conta: " + snapshot.Message; Changed?.Invoke(); }
            return snapshot;
        }
    }
}
