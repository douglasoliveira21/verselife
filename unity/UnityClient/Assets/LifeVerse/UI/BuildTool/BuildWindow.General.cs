using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using LifeVerse.Shared.Items;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>The General tab: name and description, who made and owns the object, its group, what a click does, selling it and who may do what with it.</summary>
    public sealed partial class BuildWindow
    {
        public static readonly (string Id, string Label)[] ClickActions = { ("touch", "Tocar (padrão)"), ("sit", "Sentar no objeto"), ("buy", "Comprar objeto"), ("pay", "Pagar objeto"), ("open", "Abrir objeto"), ("zoom", "Zoom no objeto"), ("ignore", "Ignorar objeto"), ("none", "Nenhuma") };
        public static readonly (string Id, string Label)[] SaleModes = { ("original", "Original"), ("copy", "Cópia"), ("contents", "Conteúdo") };

        /// <summary>Checks the sale: null when fine, otherwise why not.</summary>
        public static string SaleProblem(string mode, string price, out long amount)
        {
            amount = 0; if (!SaleModes.Any(m => m.Id == mode)) return "Escolha o que vende: o original, uma cópia ou o conteúdo.";
            if (!long.TryParse((price ?? "").Trim(), out amount) || amount < 0 || amount > 10_000_000) return "Preço inválido: use um número inteiro de 0 a 10.000.000.";
            return null;
        }

        public TextField NameField => _ui.Text("Object Name");
        public TextField DescriptionField => _ui.Text("Object Description");
        public DropdownField ClickAction => _ui.Combo("clickaction");
        public BuildSpinner PriceField => _ui.Spinner("Edit Cost");
        private string _people = ""; public string PeopleText => _people;
        private string _saleMode = "copy"; private static readonly Dictionary<Guid, string> Names = new Dictionary<Guid, string>(); private static readonly HashSet<Guid> Asking = new HashSet<Guid>();
        private readonly Dictionary<Guid, string> _groups = new Dictionary<Guid, string>(); private bool _askedGroups;

        private void BuildGeneral()
        {
            NameField.maxLength = 160; DescriptionField.maxLength = 400; if (PriceField.LabelElement != null) PriceField.LabelElement.style.width = 32f;   // "VC$" is wider than the viewer's "L$"
            NameField.RegisterValueChangedCallback(e => { if (_filling) return; var name = e.newValue.Trim(); if (name.Length == 0) { Say("O nome não pode ficar vazio.", true); return; } _b.Edit(_b.Selection.ToList(), s => s.Name = name, "renomear"); });
            DescriptionField.RegisterValueChangedCallback(e => { if (!_filling) _b.Edit(_b.Selection.ToList(), s => s.Description = e.newValue, "descrição"); });
            _ui.Unavailable("button deed", "o LifeVerse não passa a posse de um objeto ao grupo; escolher o grupo já deixa os membros trabalharem com ele.");
            OnCheck("checkbox share with group", v => SendProperties(new JsonObject { ["groupShare"] = v }));
            Choices("clickaction", ClickActions.Select(c => c.Label));
            OnPick("clickaction", i => SendProperties(new JsonObject { ["clickAction"] = ClickActions[i].Id }));
            if (ClickAction != null) ClickAction.tooltip = "A escolha é guardada no objeto; o clique no mundo ainda abre o menu (a ação direta chega numa próxima fase).";
            Choices("sale type", SaleModes.Select(s => s.Label));
            OnPick("sale type", i => { _saleMode = SaleModes[i].Id; if (_ui.Check("checkbox for sale").value) PutOnSale(); });
            OnNumber("Edit Cost", 0f, 10_000_000f, 1f, "0", _ => { if (_ui.Check("checkbox for sale").value) PutOnSale(); });
            OnCheck("checkbox for sale", v => { if (v) PutOnSale(); else SendProperties(new JsonObject { ["saleType"] = "none" }); });
            _ui.Unavailable("search_check", "precisa de um campo novo no banco de dados.");
            OnCheck("checkbox allow everyone move", v => SendProperties(new JsonObject { ["anyoneMove"] = v })); OnCheck("checkbox allow everyone copy", v => SendProperties(new JsonObject { ["anyoneCopy"] = v }));
            foreach (var name in new[] { "checkbox next owner can modify", "checkbox next owner can copy", "checkbox next owner can transfer" }) OnCheck(name, _ => SendNextRights());
        }

        private void Choices(string name, IEnumerable<string> choices) { var combo = _ui.Combo(name); if (combo != null) combo.choices = choices.ToList(); }

        private void SendNextRights()
        {
            int Bit(string name, int bit) => _ui.Check(name).value ? bit : 0;
            SendProperties(new JsonObject { ["nextOwnerRights"] = Bit("checkbox next owner can modify", ItemRights.Modify) | Bit("checkbox next owner can copy", ItemRights.Copy) | Bit("checkbox next owner can transfer", ItemRights.Transfer) });
        }

        /// <summary>Puts the selection on sale with what the tab shows.</summary>
        public void PutOnSale()
        {
            var price = PriceField.Input.value; var problem = SaleProblem(_saleMode, price == "—" || price.Length == 0 ? "0" : price, out var amount); if (problem != null) { Say(problem, true); return; }
            SendProperties(new JsonObject { ["saleType"] = _saleMode, ["salePrice"] = (int)amount });
        }

        /// <summary>The name of a person: "Você" for the one playing, known names at once, the others fetched from their public profile (and shown when they arrive).</summary>
        public string NameOf(Guid id, Action resolved)
        {
            if (id == Guid.Empty) return "—"; if (id == _c.Api.UserId) return "Você"; if (Names.TryGetValue(id, out var known)) return known;
            if (Asking.Add(id)) this.Then(_c.Api.ApiAsync("GET", "/api/v1/users/" + id + "/profile"), r =>
            {
                var shown = id.ToString().Substring(0, 8);
                if (r.Status == 200) try { using var d = JsonDocument.Parse(r.Body); if (d.RootElement.TryGetProperty("displayName", out var display) && display.GetString() is { Length: > 0 } text) shown = text; else if (d.RootElement.TryGetProperty("username", out var user) && user.GetString() is { Length: > 0 } u) shown = u; } catch (JsonException) { }
                Names[id] = shown; Asking.Remove(id); resolved?.Invoke();
            });
            return "…";
        }

        private string GroupName(Guid id)
        {
            if (_groups.TryGetValue(id, out var name)) return name;
            if (!_askedGroups) { _askedGroups = true; this.Then(_c.Api.ApiAsync("GET", "/api/v1/groups/mine"), r => { if (r.Status == 200) foreach (var row in Json.Rows(r.Body)) { var group = row.Id("id", "groupId"); if (group != Guid.Empty) _groups[group] = row.First("name"); } Refresh(); }); }
            return "Grupo " + id.ToString().Substring(0, 8);
        }

        private void PickGroup()
        {
            if (_b.Selection.Count == 0) { Say("Selecione um objeto.", true); return; }
            Child("build-group", () => new BuildGroupWindow(_c, id => SendProperties(new JsonObject { ["groupId"] = id?.ToString() })), true);
        }

        private void RefreshGeneral(List<WorldObjectState> all, WorldObjectState first)
        {
            var one = all.Count == 1; var has = first != null;
            var canName = one && (first.OwnerRights & ItemRights.Modify) != 0;
            Field(NameField, one ? first.Name : "", canName); Field(DescriptionField, one ? first.Description ?? "" : "", canName);
            // people
            void Again() { if (_b.Selection.Count > 0) Refresh(); }
            var creator = ""; var owner = "";
            if (has)
            {
                creator = all.Select(s => s.CreatorUserId).Distinct().Count() > 1 ? "(várias pessoas)" : NameOf(first.CreatorUserId, Again);
                owner = all.Select(s => s.OwnerUserId).Distinct().Count() > 1 ? "(várias pessoas)" : NameOf(first.OwnerUserId, Again);
            }
            if (_ui.Words("Creator Name") is { } made) made.text = creator; if (_ui.Words("Owner Name") is { } owns) owns.text = owner;
            _people = has ? "Criador: " + creator + "   ·   Dono: " + owner + (first.GroupId is { } g && g != Guid.Empty ? "   ·   do grupo" : "") : "";
            Face("Creator Icon", creator); Face("Owner Icon", owner);
            var groups = all.Select(s => s.GroupId).Distinct().ToList();
            if (_ui.Text("Group Name Proxy") is { } groupField) { groupField.SetValueWithoutNotify(!has ? "" : groups.Count > 1 ? "(diferentes)" : groups[0] is { } id && id != Guid.Empty ? GroupName(id) : "Nenhum"); groupField.SetEnabled(has); }
            _ui.Enable("button set group", has);
            Check("checkbox share with group", all, s => Flag(Parse(s.PermissionsJson), "groupShare", false), has);
            // the click and the sale
            var actions = all.Select(s => Text(Parse(s.PermissionsJson), "clickAction")).ToList();
            var click = _clickOf(all); Choice("clickaction", ClickActions.Select(c => c.Label).ToList(), click, has);
            var sales = all.Select(BuildEditor.SaleOf).ToList(); var selling = sales.Any(s => s.Type != "none");
            Check("checkbox for sale", all, s => BuildEditor.SaleOf(s).Type != "none", has);
            if (selling && sales[0].Type != "none") _saleMode = sales[0].Type;
            Choice("sale type", SaleModes.Select(s => s.Label).ToList(), sales.Select(s => s.Type).Distinct().Count() > 1 ? -2 : Array.FindIndex(SaleModes, m => m.Id == _saleMode), has);
            if (_ui.Spinner("Edit Cost") is { } price)
            {
                _ui.Enable("Edit Cost", has);
                if (!has) price.Blank(); else if (selling) price.Show(sales.Select(s => s.Price).Distinct().Count() > 1 ? (float?)null : sales[0].Price); else if (price.Value == null) price.Show(0f);
            }
            if (_ui.Words("perm_modify") is { } can) can.text = !has ? "" : all.All(s => (s.OwnerRights & ItemRights.Modify) != 0) ? "O objeto pode ser modificado" : "O objeto não pode ser modificado";
            Check("checkbox allow everyone move", all, s => Flag(Parse(s.PermissionsJson), "anyoneMove", false), has); Check("checkbox allow everyone copy", all, s => Flag(Parse(s.PermissionsJson), "anyoneCopy", false), has);
            Check("checkbox next owner can modify", all, s => (s.NextOwnerRights & ItemRights.Modify) != 0, has); Check("checkbox next owner can copy", all, s => (s.NextOwnerRights & ItemRights.Copy) != 0, has); Check("checkbox next owner can transfer", all, s => (s.NextOwnerRights & ItemRights.Transfer) != 0, has);
        }

        /// <summary>The index of the click action the objects share (-2 when they differ). The island keeps it beside the permissions; an object that says nothing means "touch".</summary>
        private int _clickOf(List<WorldObjectState> all)
        {
            var values = all.Select(s => string.IsNullOrEmpty(s.ClickAction) ? "touch" : s.ClickAction).Distinct().ToList();
            return values.Count > 1 ? -2 : values.Count == 0 ? 0 : Mathf.Max(0, Array.FindIndex(ClickActions, c => c.Id == values[0]));
        }

        private static void Field(TextField field, string value, bool enabled)
        {
            if (field == null) return; field.SetEnabled(enabled); if (field.focusController?.focusedElement != field) field.SetValueWithoutNotify(value);
        }

        private void Face(string name, string person)
        {
            if (_ui.Part(name) is not { } face) return; face.style.display = person.Length > 0 && person != "…" && !person.StartsWith("(", StringComparison.Ordinal) ? DisplayStyle.Flex : DisplayStyle.None;
            if (face.Q<Label>() is { } initial) initial.text = person.Length > 0 ? person.Substring(0, 1).ToUpperInvariant() : "";
        }
    }
}
