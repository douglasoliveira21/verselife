using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// "Privacidade e sessões": who may see this account online, write to it, invite it to groups and ask for its friendship; the people it blocked; and the places it is
    /// signed in. Everything is kept and enforced by the platform, so it holds on every device whether or not the settings are synced.
    /// </summary>
    public sealed class AccountPrivacyWindow : SectionWindow
    {
        public static readonly (string Id, string Text)[] Menu = { ("quem", "Quem pode"), ("bloqueados", "Bloqueados"), ("sessoes", "Sessões") };

        public const string OnlineLabel = "Quem vê que estou online", MessagesLabel = "Quem me manda mensagens", GroupsLabel = "Quem me convida para grupos", FriendsLabel = "Quem pode pedir amizade";
        private static readonly List<(string, string)> Three = new List<(string, string)> { ("everyone", "Todas as pessoas"), ("friends", "Só amigos"), ("nobody", "Ninguém") };
        private static readonly List<(string, string)> Two = new List<(string, string)> { ("everyone", "Todas as pessoas"), ("nobody", "Ninguém") };
        private static readonly (string Field, string Label, bool WithFriends)[] Choosing = { ("onlineVisibility", OnlineLabel, true), ("privateMessages", MessagesLabel, true), ("groupInvites", GroupsLabel, true), ("friendRequests", FriendsLabel, false) };

        public AccountPrivacyWindow(ApiContext context, string section = "quem") : base(context, "account-privacy", "Privacidade e sessões", Menu, 640f, 460f, new Vector2(260, 90), 150f) => Show(Menu.Any(m => m.Id == section) ? section : "quem");

        protected override void Fill(string section)
        {
            if (section == "bloqueados") Blocked(); else if (section == "sessoes") Sessions(); else Audiences();
        }

        private void Audiences()
        {
            _summary.text = "Vale para a conta, em qualquer aparelho.";
            Get("/api/v1/users/me/privacy", now =>
            {
                foreach (var (field, label, withFriends) in Choosing) Choice(label, withFriends ? Three : Two, S(now, field));
                Line("“Ninguém vê que estou online” também esconde você do mapa dos amigos. Pessoas bloqueadas não falam com você em nenhum caso.", "lv-small", "lv-muted");
                Buttons(Btn("Salvar", () =>
                {
                    var patch = new Dictionary<string, string>(); foreach (var (field, label, _) in Choosing) patch[field] = IdOf(Choices[label]);
                    Send("PUT", "/api/v1/users/me/privacy", patch, "Privacidade salva.", () => Show("quem"));
                }, "lv-btn-primary"));
            });
        }

        private void Blocked()
        {
            Get("/api/v1/social/blocks", root =>
            {
                var people = Items(root); _summary.text = people.Count == 0 ? "Você não bloqueou ninguém." : people.Count + " pessoa(s) bloqueada(s).";
                foreach (var person in people)
                {
                    var id = G(person, "userId"); var name = S(person, "displayName"); var user = S(person, "username");
                    Row(id, UiKit.Text(name, "lv-bold"), UiKit.Text("@" + user + " · desde " + When(Date(person, "blockedAt")), "lv-small", "lv-muted"), UiKit.Spacer(),
                        Sure("Desbloquear " + user, () => "Desbloquear " + name + "?", () => Send("DELETE", "/api/v1/social/blocks/" + id, null, name + " foi desbloqueada(o).", () => Show("bloqueados"))));
                }
                Line("Para bloquear alguém, use o menu da pessoa.", "lv-small", "lv-muted");
            });
        }

        private void Sessions()
        {
            Get("/api/v1/auth/sessions", root =>
            {
                var open = Items(root); _summary.text = "A conta está aberta em " + open.Count + " lugar(es).";
                foreach (var one in open)
                {
                    var id = G(one, "id"); var current = Flag(one, "current"); var device = DeviceName(S(one, "device"));
                    var cells = new List<VisualElement> { UiKit.Text(current ? device + " (este)" : device, "lv-bold"), UiKit.Text("entrou " + When(Date(one, "signedInAt")) + " · último uso " + When(Date(one, "lastSeenAt")), "lv-small", "lv-muted"), UiKit.Spacer() };
                    if (!current) cells.Add(Sure("Encerrar " + device, () => "Encerrar a sessão em " + device + "?", () => Send("DELETE", "/api/v1/auth/sessions/" + id, null, "Sessão encerrada.", () => Show("sessoes"))));
                    Row(id, cells.ToArray());
                }
                if (open.Any(o => !Flag(o, "current")))
                    Buttons(Sure("Encerrar todas as outras", () => "Encerrar a conta em todos os outros aparelhos?", () => Send("DELETE", "/api/v1/auth/sessions", null, "As outras sessões foram encerradas.", () => Show("sessoes"))));
                Line("Quem for encerrado precisa entrar de novo. Se não reconhecer uma sessão, encerre-a e troque a senha.", "lv-small", "lv-muted");
            });
        }

        /// <summary>The name a person reads for the program that signed in (what it said about itself).</summary>
        public static string DeviceName(string agent)
        {
            agent = (agent ?? "").Trim(); if (agent.Length == 0) return "Aparelho não identificado";
            if (agent.StartsWith("LifeVerse-Unity", StringComparison.OrdinalIgnoreCase)) return "Jogo" + System(agent);
            if (agent.IndexOf("Godot", StringComparison.OrdinalIgnoreCase) >= 0) return "Jogo (cliente anterior)";
            if (agent.StartsWith("Mozilla/", StringComparison.OrdinalIgnoreCase)) return "Navegador" + System(agent);
            return agent.Length > 48 ? agent.Substring(0, 48) + "…" : agent;
        }

        private static string System(string agent) => agent.Contains("Windows") ? " no Windows" : agent.Contains("Android") ? " no Android" : agent.Contains("Mac") || agent.Contains("OSX") ? " no Mac" : agent.Contains("Linux") ? " no Linux" : "";
    }
}
