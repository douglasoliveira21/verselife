using System;
using System.Collections.Generic;
using System.Text.Json;
using LifeVerse.Shared.Networking;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// What the game does with each event of the live channel: a short notice on the screen (by the formatter the Godot client uses, and only for the kinds the person keeps on), the bell counted again, a teleport invite to
    /// answer, the answer to one of ours, a balance that changed, and the event handed to the open windows (group chat, private messages) so they grow by themselves.
    /// </summary>
    public sealed class LiveEvents
    {
        private readonly ApiContext _c; private readonly UserSettings _settings;
        /// <summary>The person signed in (to know whether an accepted teleport moves them).</summary>
        public Func<Guid> Me = () => Guid.Empty;
        /// <summary>Moves the person to a spot of an island (the game decides: inside the island it asks the island, elsewhere it travels).</summary>
        public Action<Guid, float, float> MoveTo;
        /// <summary>The bell should be counted again.</summary>
        public Action BellChanged;
        public long? Balance { get; private set; }
        public event Action<long> BalanceChanged;
        public int Toasts { get; private set; }
        public int Handled { get; private set; }
        public readonly Dictionary<Guid, TeleportInviteWindow> OpenInvites = new Dictionary<Guid, TeleportInviteWindow>();

        public LiveEvents(ApiContext context, UserSettings settings) { _c = context; _settings = settings; }

        /// <summary>The category of a live event, from its type (as the Godot client sorts its toasts).</summary>
        public static string CategoryOfType(string type) => type == "group_invite" || type == "teleport_invite" ? "convites" : type != null && type.StartsWith("group_", StringComparison.Ordinal) ? "grupos" : "sistema";

        public bool ToastsOn(string category) => category == "grupos" ? _settings.ToastGroups : category == "convites" ? _settings.ToastInvites : _settings.ToastSystem;

        public void Handle(string json)
        {
            string type; JsonElement data;
            try
            {
                using var document = JsonDocument.Parse(json); var root = document.RootElement; if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String) return;
                type = t.GetString() ?? ""; data = root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? d.Clone() : default;
            }
            catch (JsonException) { return; }
            Handled++;
            switch (type)
            {
                case "teleport_invite": Invite(data); break;
                case "teleport_answer": Answered(data); break;
                case "wallet_changed": if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("balance", out var b) && b.TryGetInt64(out var balance)) { Balance = balance; BalanceChanged?.Invoke(balance); } break;
            }
            if (type != "wallet_changed" && type != "teleport_answer" && ToastsOn(CategoryOfType(type)) && NotificationFormatter.Describe(json) is { Length: > 0 } text) { _c.Notices.Show(text, NoticeKind.Info); Toasts++; }
            if (type != "wallet_changed" && type != "group_message" && type != "message") BellChanged?.Invoke();
            _c.RaiseLive(type, data, json);
        }

        private void Invite(JsonElement data)
        {
            if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("inviteId", out var i) || !Guid.TryParse(i.GetString(), out var invite) || OpenInvites.ContainsKey(invite)) return;
            var from = data.TryGetProperty("fromName", out var f) ? f.GetString() ?? "Alguém" : "Alguém"; var kind = data.TryGetProperty("kind", out var k) ? k.GetString() ?? "offer" : "offer";
            var window = new TeleportInviteWindow(_c, invite, from, kind, Accepted, () => OpenInvites.Remove(invite)); OpenInvites[invite] = window; window.Open();
        }

        /// <summary>The API answered our "accept": when this person is the one who moves, they go to the spot (a step beside the other person).</summary>
        public void Accepted(string body)
        {
            try
            {
                using var d = JsonDocument.Parse(body); var r = d.RootElement;
                if (Guid.TryParse(r.GetProperty("moverUserId").GetString(), out var mover) && mover == Me() && Guid.TryParse(r.GetProperty("islandId").GetString(), out var island)) { _c.Notices.Show("Teleporte aceito: indo até lá.", NoticeKind.Good); MoveTo?.Invoke(island, (float)r.GetProperty("x").GetDouble() + 1.5f, (float)r.GetProperty("z").GetDouble()); }
                else _c.Notices.Show("Teleporte aceito: a outra pessoa vem até você.", NoticeKind.Good);
            }
            catch (Exception exception) when (exception is JsonException || exception is KeyNotFoundException || exception is InvalidOperationException) { _c.Notices.Show("Teleporte aceito.", NoticeKind.Good); }
        }

        private void Answered(JsonElement data)
        {
            if (data.ValueKind != JsonValueKind.Object) return; var by = data.TryGetProperty("byName", out var n) ? n.GetString() ?? "A pessoa" : "A pessoa";
            if (!data.TryGetProperty("status", out var s) || s.GetString() != "accepted") { _c.Notices.Show(by + " recusou o teleporte.", NoticeKind.Info); return; }
            if (data.TryGetProperty("moverUserId", out var m) && Guid.TryParse(m.GetString(), out var mover) && mover == Me() && data.TryGetProperty("islandId", out var isl) && Guid.TryParse(isl.GetString(), out var island) && data.TryGetProperty("x", out var x) && data.TryGetProperty("z", out var z))
            { _c.Notices.Show(by + " aceitou: levando você até lá.", NoticeKind.Good); MoveTo?.Invoke(island, (float)x.GetDouble() + 1.5f, (float)z.GetDouble()); }
            else _c.Notices.Show(by + " aceitou o teleporte.", NoticeKind.Good);
        }

        /// <summary>The words for a refusal of a teleport invite (as the API names it).</summary>
        public static string InviteError(int status, string body)
        {
            string code = null; try { using var d = JsonDocument.Parse(body); if (d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("error", out var e)) code = e.GetString(); } catch (JsonException) { }
            return code switch
            {
                "not_on_island" => "A pessoa e você precisam estar na mesma ilha agora.", "island_offline" => "A ilha está desligada: não dá para teleportar.", "blocked" => "Não dá para convidar quem bloqueou você ou foi bloqueado por você.",
                "already_pending" => "Já existe um convite desse tipo esperando resposta.", "self" => "Você não pode convidar a si mesmo.", "expired" => "O convite expirou.", "already_answered" or "closed" => "Esse convite já foi respondido.", "not_found" => "O convite ou a pessoa não foi encontrado.",
                _ => status == 429 ? "Convites demais seguidos; espere um instante." : UiTasks.Explain(status, body),
            };
        }
    }

    /// <summary>"Teleporte": someone wants to bring the person to them, or to come to the person. Accept or decline; the window closes by itself after two minutes, as the invite expires.</summary>
    public sealed class TeleportInviteWindow : FloatingWindow
    {
        public const float Seconds = 120f;
        private readonly ApiContext _c; private readonly Guid _invite; private readonly Action<string> _accepted; private readonly Action _gone; private readonly Label _status; private bool _answered;
        public Guid Invite => _invite;
        public string Notice => _status.text;

        public TeleportInviteWindow(ApiContext context, Guid invite, string fromName, string kind, Action<string> accepted, Action gone) : base(context.Ui, "teleport-invite-" + invite.ToString("N").Substring(0, 8), "Teleporte", 380f, -1f, new Vector2(460, 120))
        {
            _c = context; _invite = invite; _accepted = accepted; _gone = gone; _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal;
            var text = UiKit.Text(kind == "offer" ? fromName + " quer levar você até onde está." : fromName + " pede para ir até onde você está.", "lv-bold"); text.style.whiteSpace = WhiteSpace.Normal; Body.Add(text); Body.Add(_status);
            Body.Add(UiKit.Row(UiKit.Button("Aceitar", () => Answer(true), "lv-btn-primary"), UiKit.Button("Recusar", () => Answer(false))));
            schedule.Execute(() => { if (!_answered) Close(); }).ExecuteLater((long)(Seconds * 1000)); Closed += () => _gone?.Invoke();
        }

        public void Answer(bool accept)
        {
            if (_answered) return; _answered = true;
            this.Then(_c.Api.ApiAsync("POST", "/api/v1/social/teleports/" + _invite + "/" + (accept ? "accept" : "decline"), "{}"), r =>
            {
                if (r.Status < 200 || r.Status >= 300) { _answered = false; _status.text = LiveEvents.InviteError(r.Status, r.Body); _status.AddToClassList("lv-bad"); return; }
                if (accept) _accepted?.Invoke(r.Body); else _c.Notices.Show("Teleporte recusado.", NoticeKind.Info); Close();
            });
        }
    }
}
