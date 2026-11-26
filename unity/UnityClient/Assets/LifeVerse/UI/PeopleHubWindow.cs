using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// "Pessoas", as the Godot client's <c>PeoplePanel</c> tabs Perto and Recente and its map of friends: who is around (by distance, with profile, message and going to them), who passed by lately
    /// (kept per account, at most <see cref="MaxRecent"/>), and where the friends are now in the world (<c>/api/v1/map/people</c>), with the trip checked by the server first (<c>/api/v1/map/arrival</c>).
    /// </summary>
    public sealed class PeopleHubWindow : SectionWindow
    {
        public static readonly (string Id, string Text)[] Sections = { ("perto", "Perto de você"), ("recentes", "Vistos recentemente"), ("amigos", "Amigos agora") };
        public const int MaxRecent = 20;
        private readonly Func<IEnumerable<(string Name, float Distance)>> _nearby; private readonly Func<List<string>> _recent; private readonly Action<string> _actions, _goTo;
        public int NearbyCount { get; private set; }
        public int RecentCount { get; private set; }
        public int FriendsShown { get; private set; }
        public IReadOnlyList<string> NearbyNames => _names;
        private readonly List<string> _names = new List<string>();

        /// <param name="nearby">The avatars around the person (name, metres).</param><param name="recent">The names seen lately (of this account).</param>
        /// <param name="actions">Opens what can be done with a person, by name.</param><param name="goTo">Goes to a person who is in the island, by name.</param>
        public PeopleHubWindow(ApiContext context, Func<IEnumerable<(string Name, float Distance)>> nearby, Func<List<string>> recent, Action<string> actions, Action<string> goTo)
            : base(context, "people", "Pessoas", Sections, 620f, 460f, new Vector2(380, 110), 170f)
        {
            _nearby = nearby; _recent = recent; _actions = actions; _goTo = goTo; Show("perto");
            Body.schedule.Execute(() => { if (Body.panel != null && Section == "perto") Fill("perto"); }).Every(1500);
        }

        protected override void Fill(string section)
        {
            switch (section) { case "perto": Nearby(); break; case "recentes": Recent(); break; case "amigos": Friends(); break; }
        }

        /// <summary>Who is seen now goes to the front of the recent list (no repeats, the newest first, at most <see cref="MaxRecent"/>).</summary>
        public static List<string> Remember(IEnumerable<string> recent, IEnumerable<string> seen)
        {
            var list = new List<string>(); foreach (var name in (seen ?? Enumerable.Empty<string>()).Concat(recent ?? Enumerable.Empty<string>())) if (!string.IsNullOrWhiteSpace(name) && !list.Contains(name, StringComparer.OrdinalIgnoreCase)) list.Add(name);
            return list.Take(MaxRecent).ToList();
        }

        /// <summary>"3 m", "120 m", "1,2 km".</summary>
        public static string Distance(float metres) => metres < 1000f ? Mathf.RoundToInt(metres) + " m" : (metres / 1000f).ToString("0.0", Pt) + " km";

        private void PersonRow(string name, string detail)
        {
            var who = name; var row = UiKit.Row(UiKit.Text(who, "lv-bold"), UiKit.Spacer(), UiKit.Text(detail, "lv-small", "lv-muted"));
            foreach (var (text, run) in new (string, Action)[] { ("Perfil e ações", () => _actions?.Invoke(who)), ("Ir até", () => { if (_goTo != null) { _goTo(who); Say("Indo até " + who + "…", false); } }) }) { var b = UiKit.Button(text, run); b.style.flexShrink = 0; b.style.marginLeft = 6; Actions[text + " " + who] = run; row.Add(b); }
            _page.Add(row);
        }

        private void Nearby()
        {
            var people = (_nearby?.Invoke() ?? Enumerable.Empty<(string, float)>()).OrderBy(p => p.Item2).ToList(); _names.Clear(); _names.AddRange(people.Select(p => p.Item1)); NearbyCount = people.Count;
            Title("Pessoas perto de você (" + people.Count + ")");
            if (people.Count == 0) { Line("Ninguém por perto agora.", "lv-muted"); return; }
            foreach (var (name, distance) in people) PersonRow(name, Distance(distance));
        }

        private void Recent()
        {
            var names = _recent?.Invoke() ?? new List<string>(); RecentCount = names.Count; Title("Vistos recentemente (" + names.Count + ")");
            if (names.Count == 0) { Line("Ninguém ainda: quem passar perto de você aparece aqui.", "lv-muted"); return; }
            var here = new HashSet<string>((_nearby?.Invoke() ?? Enumerable.Empty<(string, float)>()).Select(p => p.Item1), StringComparer.OrdinalIgnoreCase);
            foreach (var name in names) PersonRow(name, here.Contains(name) ? "aqui agora" : "");
            Buttons(Btn("Limpar a lista", () => { names.Clear(); Show("recentes"); }));
        }

        private void Friends()
        {
            Get("/api/v1/map/people", list =>
            {
                var friends = Items(list); FriendsShown = friends.Count; Title("Amigos no mundo agora (" + friends.Count + ")");
                if (friends.Count == 0) { Line("Nenhum amigo seu está numa ilha que você possa ver agora.", "lv-muted"); return; }
                foreach (var f in friends)
                {
                    var user = G(f, "userId"); var island = G(f, "islandId"); var x = (float)D(f, "x"); var z = (float)D(f, "z"); var name = S(f, "name");
                    Row(user, UiKit.Text(name, "lv-bold"), UiKit.Text(S(f, "islandName") + " (" + Mathf.RoundToInt(x) + ", " + Mathf.RoundToInt(z) + ")", "lv-small", "lv-muted"), Btn("Perfil " + name, () => new ProfileHubWindow(_c, user).Open()), Btn("Ir até " + name, () => Arrival.Go(_c, island, x, z, Say)));
                }
            });
        }
    }

    /// <summary>Before travelling to a spot, the server says whether this person may arrive there and where exactly (a parcel may send them to its own arrival point): <c>POST /api/v1/map/arrival</c>.</summary>
    public static class Arrival
    {
        public static string Words(string reason) => reason switch
        {
            "closed" => "A ilha está temporariamente fechada.", "full" => "A ilha está cheia no momento. Tente de novo daqui a pouco.", "private" => "Esta ilha é privada: só entra quem foi convidado.", "banned" => "Você não pode entrar nesta ilha.",
            "island_not_found" => "Esta ilha não está mais disponível.", "bad_position" => "Esse ponto fica fora da ilha.", "parcel_banned" => "Você foi impedido de entrar neste terreno.", "parcel_restricted" => "Este terreno é restrito: só entra quem foi autorizado.",
            "parcel_no_teleport" => "Este terreno não aceita teletransporte.", _ => "Não foi possível ir para esse ponto agora.",
        };

        /// <summary>The answer: allowed, the reason when not, the spot to arrive at, and whether a parcel fixed it.</summary>
        public static (bool Allowed, string Reason, float X, float Z, string Parcel, bool Forced) Read(string body)
        {
            try
            {
                using var d = JsonDocument.Parse(body); var a = d.RootElement;
                return (SectionWindow.Flag(a, "allowed"), SectionWindow.S(a, "reason"), (float)SectionWindow.D(a, "x"), (float)SectionWindow.D(a, "z"), SectionWindow.S(a, "parcelName"), SectionWindow.Flag(a, "forced"));
            }
            catch (JsonException) { return (false, "", 0f, 0f, "", false); }
        }

        /// <summary>Asks, then travels with the game's own trip (or says why not).</summary>
        public static void Go(ApiContext c, Guid island, float x, float z, Action<string, bool> say)
        {
            say?.Invoke("Verificando o acesso…", false);
            c.Ui.ScreenLayer.Then(c.Api.ApiAsync("POST", "/api/v1/map/arrival", JsonSerializer.Serialize(new { islandId = island, x, z })), r =>
            {
                if (r.Status == 404) { say?.Invoke(Words("island_not_found"), true); return; }
                if (r.Status < 200 || r.Status >= 300) { say?.Invoke(r.Status == 429 ? "Calma! Muitas tentativas. Tente de novo daqui a pouco." : UiTasks.Explain(r.Status, r.Body), true); return; }
                var a = Read(r.Body); if (!a.Allowed) { say?.Invoke(Words(a.Reason), true); return; }
                say?.Invoke(a.Forced ? (a.Parcel.Length > 0 ? a.Parcel : "Este terreno") + " leva você ao ponto de chegada dele." : "Indo para (" + Mathf.RoundToInt(a.X) + ", " + Mathf.RoundToInt(a.Z) + ")…", false);
                if (c.TravelTo != null) c.TravelTo(island, a.X, a.Z); else c.EnterIsland?.Invoke(island);
            });
        }
    }
}
