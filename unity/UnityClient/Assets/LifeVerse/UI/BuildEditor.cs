using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>The contract with the island and the API for objects: how a command is written, what an object is sent as, and what a refusal means in plain words. The server decides everything; this only explains.</summary>
    public static class BuildCommands
    {
        public static readonly JsonSerializerOptions Web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        public const string DefaultMaterial = "{\"shading\":\"blinnphong\",\"builtin\":\"plywood\",\"color\":\"#FFFFFF\",\"glossiness\":0.4}";
        public sealed class Entry { public string Type, Label; public Entry(string type, string label) { Type = type; Label = label; } }

        /// <summary>The shapes of the creation grid, in the order the person sees them (every one is a shape the island accepts).</summary>
        public static readonly Entry[] Entries =
        {
            new Entry("cube", "Caixa"), new Entry("sphere", "Esfera"), new Entry("cylinder", "Cilindro"), new Entry("prism", "Prisma"), new Entry("torus", "Toroide"), new Entry("tube", "Tubo"),
            new Entry("ring", "Anel"), new Entry("cone", "Cone"), new Entry("pyramid", "Pirâmide"), new Entry("hemisphere", "Semiesfera"), new Entry("tree", "Árvore"), new Entry("grass", "Grama"),
        };

        public static string LabelOf(string type) => Entries.FirstOrDefault(e => string.Equals(e.Type, type, StringComparison.OrdinalIgnoreCase))?.Label ?? (type == "mesh" ? "Modelo 3D" : type);

        public static string Command(string operation, Guid? objectId, int expectedRevision, object payload) => JsonSerializer.Serialize(new { operation, objectId, @object = payload, expectedRevision }, Web);

        /// <summary>The object as the island takes it in a create or an update.</summary>
        public static Dictionary<string, object> Payload(WorldObjectState s) => new Dictionary<string, object>
        {
            ["primitiveType"] = s.PrimitiveType, ["positionX"] = s.PositionX, ["positionY"] = s.PositionY, ["positionZ"] = s.PositionZ, ["rotationX"] = s.RotationX, ["rotationY"] = s.RotationY, ["rotationZ"] = s.RotationZ,
            ["scaleX"] = s.ScaleX, ["scaleY"] = s.ScaleY, ["scaleZ"] = s.ScaleZ, ["materialJson"] = s.MaterialJson, ["geometryJson"] = s.GeometryJson,
            ["physicsJson"] = string.IsNullOrWhiteSpace(s.PhysicsJson) || s.PhysicsJson == "{}" ? "{\"collision\":true}" : s.PhysicsJson, ["groupId"] = s.GroupId, ["parentObjectId"] = s.ParentObjectId, ["expectedRevision"] = s.Revision,
            ["name"] = s.Name, ["description"] = s.Description, ["contentJson"] = string.IsNullOrWhiteSpace(s.ContentJson) ? "[]" : s.ContentJson, ["pivotX"] = s.PivotX, ["pivotY"] = s.PivotY, ["pivotZ"] = s.PivotZ,
        };

        public static WorldObjectState NewState(string type, Vector3 gamePosition) => new WorldObjectState { Id = Guid.NewGuid(), PrimitiveType = type, Name = LabelOf(type), PositionX = gamePosition.x, PositionY = gamePosition.y, PositionZ = gamePosition.z, MaterialJson = DefaultMaterial, GeometryJson = "{}", PhysicsJson = "{\"collision\":true}", ContentJson = "[]", Revision = 0 };

        /// <summary>What a refusal from the island means for the person.</summary>
        public static string RejectionText(string code)
        {
            if (code != null && code.StartsWith("locked:", StringComparison.Ordinal)) return code.Substring(7) + " está editando este objeto agora. Tente de novo em instantes.";
            switch (code)
            {
                case "forbidden": return "Você não tem permissão para construir aqui.";
                case "parcel_forbidden": return "Este terreno não permite que você construa.";
                case "parcel_banned": return "Você está banido deste terreno e não pode construir nele.";
                case "scripts_not_allowed": return "Este terreno não permite scripts em objetos de quem não é o dono.";
                case "media_not_allowed": return "Este terreno não permite mídia em objetos de quem não é o dono.";
                case "not_owner": return "Este objeto não é seu: só o dono, o dono do terreno ou um administrador podem alterá-lo.";
                case "not_allowed_to_return": return "Você não pode devolver esses objetos (só o dono do terreno ou um administrador).";
                case "cost_limit": return "O custo total da ilha chegou ao limite; use formas mais simples ou apague algo.";
                case "object_quota": case "server_rejected_409": return "A ilha atingiu o limite de objetos.";
                case "parcel_object_limit": return "O terreno atingiu o limite de objetos.";
                case "parcel_cost_limit": return "O terreno atingiu o limite de custo.";
                case "invalid_object": return "O servidor recusou algum valor do objeto (tamanho, forma, textura ou conteúdo).";
                case "object_protected": return "Esse objeto está protegido contra edição acidental. Desproteja-o para mexer nele.";
                case "link_cycle": return "Esse vínculo faria uma peça ficar pendurada em si mesma.";
                case "broken_link": return "O vínculo ficaria com peças soltas: inclua as peças penduradas.";
                case "invalid_link": return "Vínculo inválido: uma peça só pode ficar pendurada em outra do mesmo conjunto.";
                case "link_too_deep": return "A hierarquia ficou funda demais (no máximo 16 níveis).";
                case "linkset_too_large": return "Um conjunto vinculado tem no máximo 256 peças.";
                case "no_modify": return "Este objeto não pode ser modificado (as peças de que é feito não permitem). Você ainda pode movê-lo, girá-lo ou devolvê-lo.";
                case "no_copy_in_use": return "Essa peça não pode ser copiada e já está em outro objeto seu.";
                case "item_busy": return "Esse item está em uso agora (oferta, venda ou colocação em andamento).";
                case "not_found": return "Esse objeto não existe mais.";
                case "cannot_raise_rights": return "Você não pode dar direitos que não tem.";
                case "not_group_member": return "Você não faz parte desse grupo.";
                case "lock_limit": return "Você está segurando objetos demais ao mesmo tempo.";
                case "revision_conflict": return "Outra pessoa alterou este objeto antes: sua edição foi recusada e a tela mostra a versão dela. Refaça a edição se ainda quiser.";
                case "rate_limit": return "Muitas alterações seguidas; aguarde um instante.";
                case "service_unavailable": return "O servidor de construção não respondeu; tente novamente.";
                case "invalid_operation": return "O servidor não entendeu o pedido de construção.";
                default: return "Construção rejeitada: " + code;
            }
        }
    }

    /// <summary>
    /// The build mode (B): click objects to select them (shift or ctrl adds), a move gizmo drawn over the scene (drag an arrow along an axis, with a grid), create, edit, duplicate, delete, undo and redo, protect and
    /// link. It never changes the world by itself: every change is a command the island checks and the API decides; what comes back is what the world shows. A selection takes a lease on the objects (the island tells the others
    /// "being edited by …" and refuses them) that is renewed while the selection is kept.
    /// </summary>
    public sealed class BuildEditor : VisualElement
    {
        private sealed class Step { public string Label; public Action Undo, Redo; }
        private sealed class Handle { public int Axis; public Vector2 From, To; public Color Colour; }

        private readonly ApiContext _c; private readonly Func<WorldClient> _client; private readonly Func<Camera> _camera; private readonly Func<Vector3> _where; private readonly Func<TerrainView> _terrain;
        private readonly List<Guid> _selected = new List<Guid>(); private readonly Dictionary<Guid, string> _heldBy = new Dictionary<Guid, string>(); private readonly List<Step> _undo = new List<Step>(), _redo = new List<Step>();
        private readonly Queue<Action<WorldObjectState>> _created = new Queue<Action<WorldObjectState>>(); private readonly List<Handle> _handles = new List<Handle>();
        private float _renew; private int _dragAxis = -1; private Vector2 _dragFrom; private readonly Dictionary<Guid, Vector3> _dragStart = new Dictionary<Guid, Vector3>(); private float _dragAmount; private bool _hooked; private WorldClient _hookedTo;

        public bool Active { get; private set; }
        /// <summary>While on, a click on an object picks the face under the cursor (for a colour or picture on that face only).</summary>
        public bool FacePick { get; set; }
        public int PickedFace { get; private set; } = -1;
        /// <summary>The first-use tutorial: eight small tasks that finish by themselves when the person does them.</summary>
        public LifeVerse.Shared.Building.BuildTutorial Tutorial { get; } = new LifeVerse.Shared.Building.BuildTutorial();
        public void Did(LifeVerse.Shared.Building.TutorialStep step) { if (Tutorial.Notify(step)) Changed?.Invoke(); }
        public float Grid { get; set; } = .25f;
        /// <summary>Dragging lands on multiples of <see cref="Grid"/> ("Encaixar na grade"); off, it follows the mouse freely (the arrow keys still move by the grid step).</summary>
        public bool SnapToGrid { get; set; } = true;
        public IReadOnlyList<Guid> Selection => _selected;
        public int[] FacesOfSelection() => _selected.Count > 0 && Objects != null ? Objects.FacesOf(_selected[0]) : new int[0];
        public int UndoCount => _undo.Count;
        public int RedoCount => _redo.Count;
        public string LastMessage { get; private set; } = "";
        public string LastRejection { get; private set; }
        public int Created { get; private set; }
        public bool Dragging => _dragAxis >= 0;
        public event Action Changed;
        /// <summary>The island sold something to this person: what it cost, what kind of sale, and what they received.</summary>
        public event Action<long, string, IReadOnlyList<string>> Purchased;
        public event Action<string> Said;

        public BuildEditor(ApiContext context, Func<WorldClient> client, Func<Camera> camera, Func<Vector3> where, Func<TerrainView> terrain)
        {
            _c = context; _client = client; _camera = camera; _where = where; _terrain = terrain;
            name = "build-overlay"; pickingMode = PickingMode.Ignore; style.position = Position.Absolute; style.left = style.top = style.right = style.bottom = 0; generateVisualContent += Draw;
        }

        private WorldObjects Objects => _client()?.Objects;
        private Guid Me => _c.Api.UserId;
        public WorldObjectState StateOf(Guid id) => Objects?.StateOf(id);
        // edits made before the island has answered the last one (a slider being dragged) build on what was just sent, with the next revision, or the island would refuse each of them for being based on an old one
        private readonly Dictionary<Guid, WorldObjectState> _optimistic = new Dictionary<Guid, WorldObjectState>();
        public WorldObjectState Latest(Guid id)
        {
            var server = StateOf(id); if (_optimistic.TryGetValue(id, out var guess)) { if (server == null || guess.Revision > server.Revision) return guess; _optimistic.Remove(id); } return server;
        }
        public int InFlight => _optimistic.Count(p => { var s = StateOf(p.Key); return s != null && p.Value.Revision > s.Revision; });
        public float? GroundAt(float x, float z) => _terrain()?.GroundHeight(x, z);
        public bool HeldByOther(Guid id, out string who) => _heldBy.TryGetValue(id, out who);

        // ---- the outbox: the island accepts 20 build commands a second, so a burst (a saved construction of hundreds of pieces, a long drag) is spread out instead of refused ----
        private readonly Queue<string> _outbox = new Queue<string>(); private readonly Queue<float> _sentAt = new Queue<float>(); public const int PerSecond = 12;
        public int Waiting => _outbox.Count;

        private bool Submit(string json) { if (_client() == null) return false; _outbox.Enqueue(json); Drain(); return true; }

        private void Drain()
        {
            var now = Time.realtimeSinceStartup; while (_sentAt.Count > 0 && now - _sentAt.Peek() > 1f) _sentAt.Dequeue();
            while (_outbox.Count > 0 && _sentAt.Count < PerSecond && _client() != null) { _client().SendBuild(_outbox.Dequeue()); _sentAt.Enqueue(now); }
        }

        private void Say(string text) { LastMessage = text; Said?.Invoke(text); }

        /// <summary>Starts or ends the build mode. Leaving it gives up the selection (and its leases).</summary>
        public void SetActive(bool active)
        {
            if (active == Active) return; Active = active; Hook();
            if (active) { if (parent == null) _c.Ui.HudLayer.Add(this); Say("Modo de construção: clique num objeto para selecioná-lo; B sai."); }
            else { Clear(); RemoveFromHierarchy(); _dragAxis = -1; }
            Changed?.Invoke();
        }

        private void Hook()
        {
            var client = _client(); if (client == null || client == _hookedTo) return;
            if (_hookedTo != null) { _hookedTo.BuildOperation -= OnOperation; _hookedTo.BuildRejected -= OnRejected; }
            _hookedTo = client; client.BuildOperation += OnOperation; client.BuildRejected += OnRejected; _hooked = true;
        }

        // ---- what the island says ----

        /// <summary>One operation announced by the island (public so a test can feed the same JSON).</summary>
        public string LastSent { get; private set; } = "";
        public int OpsSeen { get; private set; }
        public string LastOp { get; private set; } = "";
        public void OnOperation(string json)
        {
            OpsSeen++; LastOp = json.Length > 200 ? json.Substring(0, 200) : json;
            try
            {
                using var document = JsonDocument.Parse(json); var root = document.RootElement; var operation = root.TryGetProperty("operation", out var op) ? op.GetString() : null;
                if (operation == "live") { ShowLive(root); return; }
                if (operation == "lock" || operation == "unlock")
                {
                    var user = root.TryGetProperty("userId", out var u) && Guid.TryParse(u.GetString(), out var g) ? g : Guid.Empty; var userName = root.TryGetProperty("userName", out var n) ? n.GetString() ?? "outra pessoa" : "outra pessoa";
                    if (root.TryGetProperty("objectIds", out var ids) && ids.ValueKind == JsonValueKind.Array)
                        foreach (var item in ids.EnumerateArray()) if (Guid.TryParse(item.GetString(), out var id)) { if (operation == "unlock" || user == Me) _heldBy.Remove(id); else _heldBy[id] = userName; if (operation == "unlock" && user != Me) Objects?.Refresh(id); }   // a drag that ended without a save: the object goes back to what the island has
                    Changed?.Invoke(); return;
                }
                if (operation == "bought" && root.TryGetProperty("buyerUserId", out var buyer) && Guid.TryParse(buyer.GetString(), out var buyerId) && buyerId == Me && !(root.TryGetProperty("replay", out var replay) && replay.ValueKind == JsonValueKind.True))
                    Purchased?.Invoke(root.TryGetProperty("price", out var priced) && priced.TryGetInt64(out var cost) ? cost : 0, root.TryGetProperty("saleType", out var kind) ? kind.GetString() ?? "original" : "original", root.TryGetProperty("delivered", out var delivered) && delivered.ValueKind == JsonValueKind.Array ? delivered.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>());
                if (operation == "create" && root.TryGetProperty("objectState", out var state) && state.ValueKind == JsonValueKind.Object)
                {
                    var made = WorldObjectState.Parse(state);
                    if (made.CreatorUserId == Me && _created.Count > 0) { Created++; _created.Dequeue()(made); }
                }
                Changed?.Invoke();
            }
            catch (Exception exception) when (exception is JsonException || exception is KeyNotFoundException || exception is InvalidOperationException) { }
        }

        private void OnRejected(string code) { LastRejection = code; _created.Clear(); _optimistic.Clear(); Say(BuildCommands.RejectionText(code)); foreach (var id in _selected) Objects?.Refresh(id); }

        // ---- selection ----

        public void Select(IEnumerable<Guid> ids, bool add = false)
        {
            var wanted = ids.Where(id => Objects != null && Objects.Has(id)).Distinct().ToList(); var before = _selected.ToList();
            if (add) foreach (var id in wanted) { if (!_selected.Remove(id)) _selected.Add(id); } else { _selected.Clear(); _selected.AddRange(wanted); }
            var dropped = before.Where(id => !_selected.Contains(id)).ToList(); if (dropped.Count > 0) Lock("unlock", dropped);
            var taken = _selected.Where(id => !before.Contains(id)).ToList(); if (taken.Count > 0) Lock("lock", taken); _renew = 0f; Changed?.Invoke();
        }

        public void Clear() => Select(Array.Empty<Guid>());

        private void Lock(string operation, List<Guid> ids) => Submit(JsonSerializer.Serialize(new { operation, objectIds = ids, expectedRevision = 0 }, BuildCommands.Web));

        // ---- commands ----

        private bool Send(string operation, WorldObjectState state) => _client() != null && Submit(BuildCommands.Command(operation, state.Id, state.Revision, operation == "delete" ? null : BuildCommands.Payload(state)));

        /// <summary>Where a new object goes: a few metres in front of the camera, on the ground.</summary>
        public Vector3 PlacePoint(float halfHeight = .5f)
        {
            var camera = _camera(); var origin = _where(); var forward = camera != null ? new Vector3(camera.transform.forward.x, 0f, -camera.transform.forward.z) : Vector3.forward; if (forward.sqrMagnitude < .001f) forward = Vector3.forward;
            var point = origin + forward.normalized * 4f; var ground = _terrain()?.GroundHeight(point.x, point.z); point.y = (ground ?? origin.y) + halfHeight; return point;
        }

        /// <summary>Asks the island for a new object of a shape; when it is confirmed it becomes the selection.</summary>
        public bool Create(string type, Vector3? at = null, Action<WorldObjectState> created = null) => CreateState(BuildCommands.NewState(type, at ?? PlacePoint()), created, true);

        /// <summary>Asks the island for an object made from a whole state (a copy, a piece of a saved construction). <paramref name="select"/> makes the new object the selection.</summary>
        public bool CreateState(WorldObjectState template, Action<WorldObjectState> created = null, bool select = true, bool record = true)
        {
            if (_client() == null) return false; template.Revision = 0;
            LastSent = BuildCommands.Command("create", null, 0, BuildCommands.Payload(template)); if (!Submit(LastSent)) return false;
            _created.Enqueue(made => { Did(LifeVerse.Shared.Building.TutorialStep.Create); if (select) Select(new[] { made.Id }); if (record) { var madeId = made.Id; var copy = template.Copy(); Push("criar " + made.Name, () => Delete(new[] { madeId }, false), () => CreateState(copy.Copy(), null, true, false), false); } created?.Invoke(made); });
            return true;
        }
        /// <summary>Changes objects with <paramref name="change"/> (a copy of each is sent) and remembers how to take it back.</summary>
        public bool Edit(IEnumerable<Guid> ids, Action<WorldObjectState> change, string label, bool record = true)
        {
            var steps = new List<KeyValuePair<WorldObjectState, WorldObjectState>>();
            foreach (var id in ids.ToList())
            {
                var current = Latest(id); if (current == null) continue; if (_heldBy.TryGetValue(id, out var who)) { Say(BuildCommands.RejectionText("locked:" + who)); return false; }
                var before = current.Copy(); var after = current.Copy(); change(after); if (!Send("update", after)) return false; var guess = after.Copy(); guess.Revision = after.Revision + 1; _optimistic[id] = guess; steps.Add(new KeyValuePair<WorldObjectState, WorldObjectState>(before, after));
            }
            if (steps.Count == 0) return false;
            { var b = steps[0].Key; var a = steps[0].Value; if (b.PositionX != a.PositionX || b.PositionY != a.PositionY || b.PositionZ != a.PositionZ) Did(LifeVerse.Shared.Building.TutorialStep.Move); if (b.ScaleX != a.ScaleX || b.ScaleY != a.ScaleY || b.ScaleZ != a.ScaleZ) Did(LifeVerse.Shared.Building.TutorialStep.Resize); if (b.RotationX != a.RotationX || b.RotationY != a.RotationY || b.RotationZ != a.RotationZ) Did(LifeVerse.Shared.Building.TutorialStep.Rotate); if (b.MaterialJson != a.MaterialJson) Did(LifeVerse.Shared.Building.TutorialStep.Texture); }
            if (record) Push(label, () => { foreach (var s in steps) Restore(s.Key); }, () => { foreach (var s in steps) Restore(s.Value); }, true);
            return true;
        }

        private void Restore(WorldObjectState target)
        {
            var current = Latest(target.Id); if (current == null) return; var next = current.Copy();
            next.PositionX = target.PositionX; next.PositionY = target.PositionY; next.PositionZ = target.PositionZ; next.RotationX = target.RotationX; next.RotationY = target.RotationY; next.RotationZ = target.RotationZ;
            next.ScaleX = target.ScaleX; next.ScaleY = target.ScaleY; next.ScaleZ = target.ScaleZ; next.MaterialJson = target.MaterialJson; next.GeometryJson = target.GeometryJson; next.PhysicsJson = target.PhysicsJson;
            next.ContentJson = target.ContentJson; next.Name = target.Name; next.Description = target.Description; Send("update", next); var guessed = next.Copy(); guessed.Revision = next.Revision + 1; _optimistic[next.Id] = guessed;
        }

        public bool Delete(IEnumerable<Guid> ids, bool record = true)
        {
            var list = ids.ToList(); var gone = new List<WorldObjectState>(); var ok = false;
            foreach (var id in list)
            {
                var state = StateOf(id); if (state == null) continue; if (_heldBy.TryGetValue(id, out var who)) { Say(BuildCommands.RejectionText("locked:" + who)); continue; }
                gone.Add(state.Copy()); ok |= _client() != null && Submit(BuildCommands.Command("delete", id, state.Revision, null));
            }
            if (ok) { _selected.RemoveAll(id => list.Contains(id)); Changed?.Invoke(); }
            if (ok && record) Push("apagar", () => { foreach (var s in gone) CreateState(s.Copy(), null, false, false); }, () => Delete(gone.Select(s => s.Id).ToList(), false), false);
            return ok;
        }

        /// <summary>Copies the selection one metre or more to the side; the copies become the selection.</summary>
        public int Duplicate()
        {
            var made = new List<Guid>(); var sources = _selected.Select(StateOf).Where(s => s != null).ToList(); var step = Mathf.Max(1f, Grid * 4f);
            foreach (var s in sources) { var copy = s.Copy(); copy.PositionX += step; copy.GroupId = null; copy.ParentObjectId = null; CreateState(copy, m => { made.Add(m.Id); if (made.Count == sources.Count) Select(made); }, false, true); }
            if (sources.Count > 0) Did(LifeVerse.Shared.Building.TutorialStep.Duplicate); return sources.Count;
        }
        /// <summary>Changes who may do what with objects (rights of the next owner, sale, click action, group). Only what is in <paramref name="properties"/> changes; the island and the API decide who may.</summary>
        public bool SendProperties(IReadOnlyList<Guid> ids, System.Text.Json.Nodes.JsonObject properties) => ids.Count > 0 && properties.Count > 0 && _client() != null && Submit(JsonSerializer.Serialize(new { operation = "properties", objectIds = ids, expectedRevision = 0, properties = System.Text.Json.Nodes.JsonNode.Parse(properties.ToJsonString()) }, BuildCommands.Web));

        /// <summary>The sale of an object as its permissions say: the kind ("none", "original", "copy", "contents") and the price.</summary>
        public static (string Type, long Price) SaleOf(WorldObjectState s)
        {
            try { var p = System.Text.Json.Nodes.JsonNode.Parse(string.IsNullOrWhiteSpace(s.PermissionsJson) ? "{}" : s.PermissionsJson) as System.Text.Json.Nodes.JsonObject; var type = p?["saleType"]?.GetValue<string>() ?? "none"; var price = p?["salePrice"] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<long>(out var n) ? n : 0; return (type, price); }
            catch (Exception exception) when (exception is JsonException || exception is InvalidOperationException || exception is FormatException) { return ("none", 0); }
        }

        /// <summary>The price an object asks to be paid (0 when it does not take payments).</summary>
        public static long PayPriceOf(WorldObjectState s)
        {
            try { var p = System.Text.Json.Nodes.JsonNode.Parse(string.IsNullOrWhiteSpace(s.PermissionsJson) ? "{}" : s.PermissionsJson) as System.Text.Json.Nodes.JsonObject; return p?["payPrice"] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<long>(out var n) ? n : 0; }
            catch (Exception exception) when (exception is JsonException || exception is InvalidOperationException || exception is FormatException) { return 0; }
        }

        /// <summary>Buys an object that is for sale: the island charges the price that is on it now, and a price that moved is refused (the key makes a repeated press the same purchase).</summary>
        public bool Buy(Guid id, long expectedPrice, string idempotencyKey = null) => Submit(JsonSerializer.Serialize(new { operation = "buy", objectId = id, expectedRevision = 0, properties = new { idempotencyKey = idempotencyKey ?? Guid.NewGuid().ToString("N"), expectedPrice } }, BuildCommands.Web));

        /// <summary>Sends objects back to their owners' inventories (only the owner of the land or an administrator may).</summary>
        public bool Return(IEnumerable<Guid> ids) { var list = ids.Distinct().ToList(); return list.Count > 0 && Submit(JsonSerializer.Serialize(new { operation = "return", objectIds = list, expectedRevision = 0 }, BuildCommands.Web)); }

        /// <summary>Protects (or releases) the selection against accidental editing.</summary>
        public bool Protect(bool locked) => _selected.Count > 0 && _client() != null && Submit(JsonSerializer.Serialize(new { operation = "protect", objectIds = _selected, expectedRevision = 0, locked }, BuildCommands.Web));

        /// <summary>Hangs the other selected objects under the first one, in a group of their own (a linkset); or frees them.</summary>
        public bool Link(bool join)
        {
            if (_selected.Count == 0 || (join && _selected.Count < 2)) return false; var group = join ? Guid.NewGuid() : (Guid?)null; var parentId = join ? _selected[0] : (Guid?)null;
            var links = _selected.Select(StateOf).Where(s => s != null).Select(s => new { objectId = s.Id, groupId = join ? group : null, parentId = join && s.Id != parentId ? parentId : null, expectedRevision = s.Revision }).ToList();
            if (join) Did(LifeVerse.Shared.Building.TutorialStep.Link); return _client() != null && Submit(JsonSerializer.Serialize(new { operation = "structure", links, expectedRevision = 0 }, BuildCommands.Web));
        }

        // ---- constructions kept in the inventory ----

        /// <summary>Saves the selection (it must be the person's own) as one construction in the inventory; with <paramref name="take"/> the objects also leave the world once saved.</summary>
        public void SavePackage(string name, bool take, Action<string> done)
        {
            var states = _selected.Select(StateOf).Where(s => s != null).ToList();
            if (states.Count == 0) { done("Selecione os objetos que quer guardar."); return; }
            if (states.Count > LifeVerse.Shared.Building.BuildPackages.MaxObjects) { done("Uma construção guarda até " + LifeVerse.Shared.Building.BuildPackages.MaxObjects + " objetos."); return; }
            var notMine = states.FirstOrDefault(s => Me != Guid.Empty && s.OwnerUserId != Me); if (notMine != null) { done("\"" + notMine.Name + "\" não é seu: só dá para guardar objetos seus."); return; }
            name = string.IsNullOrWhiteSpace(name) ? (states.Count == 1 ? states[0].Name : "Construção") : name.Trim();
            var sources = states.Select(s => new LifeVerse.Shared.Building.SourceObject(s.PrimitiveType, s.Name, s.Description, s.PositionX, s.PositionY, s.PositionZ, s.RotationX, s.RotationY, s.RotationZ, s.ScaleX, s.ScaleY, s.ScaleZ, s.MaterialJson, s.GeometryJson, s.PhysicsJson, s.ContentJson, s.GroupId, s.Id, s.ParentObjectId, s.PivotX, s.PivotY, s.PivotZ)).ToList();
            var json = LifeVerse.Shared.Building.BuildPackages.Serialize(LifeVerse.Shared.Building.BuildPackages.Create(name, sources)); var ids = states.Select(s => s.Id).ToList();
            this.Then(_c.Api.ApiAsync("POST", "/api/v1/inventory/build-packages", json), r =>
            {
                if (r.Status < 200 || r.Status >= 300) { done("O servidor recusou a construção (" + UiTasks.Explain(r.Status, r.Body) + ")."); return; }
                LastSavedItem = Json.One(r.Body).Id("itemId", "id"); Did(LifeVerse.Shared.Building.TutorialStep.Save); Did(LifeVerse.Shared.Building.TutorialStep.Save); if (take) Delete(ids, false);
                done("\"" + name + "\" guardada no inventário (" + states.Count + " objeto(s))" + (take ? " e retirada do mundo." : "."));
            });
        }

        public Guid LastSavedItem { get; private set; }

        /// <summary>Puts a construction of the inventory in the world in front of the person. One that cannot be copied is placed by the island itself.</summary>
        public void PlacePackage(Guid itemId, Action<string> done)
        {
            var ground = PlacePoint(0f);
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/inventory/build-packages/" + itemId, null), r =>
            {
                if (r.Status == 403 && (r.Body ?? "").Contains("use_rez")) { if (_client() != null && Submit(JsonSerializer.Serialize(new { operation = "rez", packageItemId = itemId, originX = ground.x, originY = ground.y, originZ = ground.z, expectedRevision = 0 }, BuildCommands.Web))) done("Pedindo à ilha para colocar a construção…"); return; }
                if (r.Status != 200) { done("Não foi possível abrir a construção (" + UiTasks.Explain(r.Status, r.Body) + ")."); return; }
                var package = LifeVerse.Shared.Building.BuildPackages.Parse(r.Body, out var error); if (package == null) { done("Construção inválida: " + error); return; }
                var placed = LifeVerse.Shared.Building.BuildPackages.Place(package, new System.Numerics.Vector3(ground.x, ground.y, ground.z)); var made = new List<Guid>(); var total = placed.Count;
                foreach (var p in placed)
                {
                    var o = p.Source; var state = new WorldObjectState { Id = Guid.NewGuid(), PrimitiveType = o.PrimitiveType, Name = o.Name, Description = o.Description ?? "", PositionX = p.Position.X, PositionY = p.Position.Y, PositionZ = p.Position.Z, RotationX = o.RotationX, RotationY = o.RotationY, RotationZ = o.RotationZ, ScaleX = o.ScaleX, ScaleY = o.ScaleY, ScaleZ = o.ScaleZ, MaterialJson = o.MaterialJson, GeometryJson = o.GeometryJson, PhysicsJson = o.PhysicsJson, ContentJson = o.ContentJson, GroupId = p.GroupId, PivotX = o.PivotX, PivotY = o.PivotY, PivotZ = o.PivotZ };
                    CreateState(state, m => { made.Add(m.Id); if (made.Count == total) { Select(made); done("Construção colocada (" + total + " objeto(s))."); } }, false, false);
                }
                done("Colocando a construção: " + total + " objeto(s)…");
            });
        }
        // ---- history ----

        private void Push(string label, Action undo, Action redo, bool _) { _undo.Add(new Step { Label = label, Undo = undo, Redo = redo }); if (_undo.Count > 50) _undo.RemoveAt(0); _redo.Clear(); Changed?.Invoke(); }
        public bool Undo() { if (_undo.Count == 0) return false; var step = _undo[_undo.Count - 1]; _undo.RemoveAt(_undo.Count - 1); step.Undo(); _redo.Add(step); Say("Desfeito: " + step.Label); Changed?.Invoke(); return true; }
        public bool Redo() { if (_redo.Count == 0) return false; var step = _redo[_redo.Count - 1]; _redo.RemoveAt(_redo.Count - 1); step.Redo(); _undo.Add(step); Say("Refeito: " + step.Label); Changed?.Invoke(); return true; }

        // ---- the scene: bounds, projection, gizmo ----

        public bool BoundsOf(Guid id, out UnityEngine.Bounds bounds)
        {
            bounds = default; var go = Objects?.ViewOf(id); if (go == null) return false; var any = false;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>()) { if (renderer == null) continue; if (!any) { bounds = renderer.bounds; any = true; } else bounds.Encapsulate(renderer.bounds); }
            if (!any) bounds = new UnityEngine.Bounds(go.transform.position, Vector3.one); return true;
        }

        private UnityEngine.Bounds SelectionBounds() { var any = false; var all = default(UnityEngine.Bounds); foreach (var id in _selected) if (BoundsOf(id, out var b)) { if (!any) { all = b; any = true; } else all.Encapsulate(b); } return all; }

        private Vector2 ToPanel(Vector3 world) { var screen = _camera().WorldToScreenPoint(world); var scale = _c.Ui.Panel.scale <= 0f ? 1f : _c.Ui.Panel.scale; return new Vector2(screen.x / scale, (Screen.height - screen.y) / scale); }
        private static readonly Vector3[] AxesUnity = { new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, -1) };   // game x, y, z in Unity space
        private static readonly Color[] AxisColours = { new Color(.95f, .3f, .3f), new Color(.35f, .9f, .4f), new Color(.35f, .55f, 1f) };

        private void BuildHandles()
        {
            _handles.Clear(); var camera = _camera(); if (_selected.Count == 0 || camera == null) return; var box = SelectionBounds(); var centre = box.center; var distance = Vector3.Distance(camera.transform.position, centre); var length = Mathf.Clamp(distance * .18f, .8f, 12f);
            if (Vector3.Dot(camera.transform.forward, centre - camera.transform.position) <= 0f) return;
            for (var axis = 0; axis < 3; axis++) _handles.Add(new Handle { Axis = axis, From = ToPanel(centre), To = ToPanel(centre + AxesUnity[axis] * length), Colour = AxisColours[axis] });
        }

        // ---- every frame ----

        /// <summary>The game calls this each frame while the build mode is on: the mouse, the keys, the lease renewal and the picture.</summary>
        public void Tick(float dt)
        {
            Drain(); if (!Active) return; Hook();
            if (_selected.Count > 0) { _renew += dt; if (_renew > 8f) { _renew = 0f; Lock("lock", _selected.ToList()); } }
            _selected.RemoveAll(id => Objects == null || !Objects.Has(id));
            BuildHandles(); var mouse = Mouse.current; var keyboard = Keyboard.current; var typing = _c.Ui.Root.panel?.focusController?.focusedElement is TextField;
            if (mouse != null && _camera() != null)
            {
                var screen = mouse.position.ReadValue(); var panel = new Vector2(screen.x, Screen.height - screen.y) / Mathf.Max(.01f, _c.Ui.Panel.scale);
                if (mouse.leftButton.wasPressedThisFrame && !UiKit.IsOver(_c.Ui.Root, screen) && !typing) Press(screen, panel, keyboard != null && (keyboard.shiftKey.isPressed || keyboard.ctrlKey.isPressed));
                else if (_dragAxis >= 0 && mouse.leftButton.isPressed) DragTo(panel, keyboard != null && keyboard.altKey.isPressed);
                else if (_dragAxis >= 0) EndDrag();
            }
            if (keyboard != null && !typing)
            {
                var ctrl = keyboard.ctrlKey.isPressed;
                if (keyboard.deleteKey.wasPressedThisFrame && _selected.Count > 0) Delete(_selected.ToList());
                if (keyboard.escapeKey.wasPressedThisFrame) Clear();
                if (ctrl && keyboard.zKey.wasPressedThisFrame) { if (keyboard.shiftKey.isPressed) Redo(); else Undo(); }
                if (ctrl && keyboard.yKey.wasPressedThisFrame) Redo();
                if (ctrl && keyboard.dKey.wasPressedThisFrame) Duplicate();
                if (_selected.Count > 0 && !ctrl) Nudge(keyboard);
            }
            MarkDirtyRepaint();
        }

        private void Nudge(Keyboard keyboard)
        {
            var dx = (keyboard.rightArrowKey.wasPressedThisFrame ? 1 : 0) - (keyboard.leftArrowKey.wasPressedThisFrame ? 1 : 0); var dz = (keyboard.upArrowKey.wasPressedThisFrame ? 1 : 0) - (keyboard.downArrowKey.wasPressedThisFrame ? 1 : 0);
            var dy = (keyboard.pageUpKey.wasPressedThisFrame ? 1 : 0) - (keyboard.pageDownKey.wasPressedThisFrame ? 1 : 0);
            if (dx != 0 || dz != 0 || dy != 0) Move(new Vector3(dx, dy, -dz) * Grid);
        }

        /// <summary>Moves the selection by an amount in game space.</summary>
        public bool Move(Vector3 gameDelta) => Edit(_selected.ToList(), s => { s.PositionX += gameDelta.x; s.PositionY += gameDelta.y; s.PositionZ += gameDelta.z; }, "mover");

        /// <summary>Turns the selection around the vertical axis (degrees).</summary>
        public bool Turn(float degrees) => Edit(_selected.ToList(), s => s.RotationY = Mathf.Repeat(s.RotationY + degrees + 180f, 360f) - 180f, "girar");

        private void Press(Vector2 screen, Vector2 panel, bool add)
        {
            foreach (var h in _handles) if (DistanceToSegment(panel, h.From, h.To) < 12f && (panel - h.From).magnitude > 8f) { StartDrag(h, panel); return; }
            var camera = _camera(); if (camera == null || Objects == null) return;
            if (Objects.Pick(camera.ScreenPointToRay(screen), 300f, out var id, out _))
            {
                if (FacePick && _selected.Contains(id) && Objects.PickFace(id, camera.ScreenPointToRay(screen), out var face)) { PickedFace = face; Say("Face " + face + " escolhida."); Changed?.Invoke(); return; }
                Select(new[] { id }, add);
            }
            else if (!add) Clear();
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; var t = ab.sqrMagnitude < .0001f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude); return (p - (a + ab * t)).magnitude;
        }

        public enum GizmoMode { Move, Rotate, Scale }
        /// <summary>The steps of turning (degrees) and stretching (metres) with the handles (Configurações › Construção).</summary>
        public float RotateStep = 15f, ScaleStep = .1f;
        /// <summary>What the coloured handles do: move along an axis, turn around it, or stretch along it.</summary>
        public GizmoMode Mode { get; set; } = GizmoMode.Move;
        private readonly Dictionary<Guid, Vector3> _dragRotation = new Dictionary<Guid, Vector3>(), _dragScale = new Dictionary<Guid, Vector3>();

        private void StartDrag(Handle handle, Vector2 panel)
        {
            if (_selected.Any(id => _heldBy.ContainsKey(id))) { Say(BuildCommands.RejectionText("locked:" + _heldBy[_selected.First(id => _heldBy.ContainsKey(id))])); return; }
            _dragAxis = handle.Axis; _dragFrom = panel; _dragAmount = 0f; _dragStart.Clear(); _dragRotation.Clear(); _dragScale.Clear();
            foreach (var id in _selected) { var go = Objects.ViewOf(id); var s = StateOf(id); if (go != null && s != null) { _dragStart[id] = go.transform.position; _dragRotation[id] = new Vector3(s.RotationX, s.RotationY, s.RotationZ); _dragScale[id] = new Vector3(s.ScaleX, s.ScaleY, s.ScaleZ); } }
        }

        /// <summary>The value a drag stands for: metres to move or stretch, degrees to turn.</summary>
        public float AmountFor(float screenMetres, float screenPixels, bool free)
        {
            if (Mode == GizmoMode.Rotate) { var degrees = screenPixels * .5f; return free || RotateStep <= 0f ? degrees : Mathf.Round(degrees / RotateStep) * RotateStep; }
            if (Mode == GizmoMode.Scale) return free || ScaleStep <= 0f ? screenMetres : Mathf.Round(screenMetres / ScaleStep) * ScaleStep;
            return free || !SnapToGrid || Grid <= 0f ? screenMetres : Mathf.Round(screenMetres / Grid) * Grid;
        }

        private void DragTo(Vector2 panel, bool free)
        {
            var handle = _handles.FirstOrDefault(h => h.Axis == _dragAxis); if (handle == null) return; var screenAxis = handle.To - handle.From; var length = screenAxis.magnitude; if (length < 1f) return;
            var pixels = Vector2.Dot(panel - _dragFrom, screenAxis / length); var metres = pixels / (length / Mathf.Max(.1f, GizmoLength())); _snapTarget = null; _dragAmount = AmountFor(metres, pixels, free);
            if (Mode == GizmoMode.Move && !free && SnapMagnet) { var near = SnapAmount(_dragAxis, metres); if (near.HasValue) _dragAmount = near.Value; }
            foreach (var pair in _dragStart)
            {
                var go = Objects.ViewOf(pair.Key); if (go == null) continue;
                if (Mode == GizmoMode.Move) go.transform.position = pair.Value + AxesUnity[_dragAxis] * _dragAmount;
                else if (Mode == GizmoMode.Rotate) { var r = _dragRotation[pair.Key]; r[_dragAxis] += _dragAmount; go.transform.rotation = GameSpace.RotationToUnity(r.x, r.y, r.z); }
                else { var sc = _dragScale[pair.Key]; sc[_dragAxis] = Mathf.Clamp(sc[_dragAxis] + _dragAmount, .01f, 64f); go.transform.localScale = new Vector3(sc.x, sc.y, sc.z); }
            }
            SendLiveIfDue();
        }

        private float GizmoLength() { var camera = _camera(); return Mathf.Clamp(Vector3.Distance(camera.transform.position, SelectionBounds().center) * .18f, .8f, 12f); }

        private void EndDrag()
        {
            var axis = _dragAxis; var amount = _dragAmount; _dragAxis = -1; var mode = Mode; _snapTarget = null;
            foreach (var pair in _dragStart) { var go = Objects.ViewOf(pair.Key); var s = StateOf(pair.Key); if (go != null && s != null) { go.transform.position = pair.Value; go.transform.rotation = GameSpace.RotationToUnity(s.RotationX, s.RotationY, s.RotationZ); go.transform.localScale = new Vector3(Mathf.Max(s.ScaleX, .001f), Mathf.Max(s.ScaleY, .001f), Mathf.Max(s.ScaleZ, .001f)); } }   // back to what the island last confirmed; the answer moves it
            _dragStart.Clear();
            if (Mathf.Abs(amount) < .0005f) return;
            if (mode == GizmoMode.Move) Move(new Vector3(axis == 0 ? amount : 0f, axis == 1 ? amount : 0f, axis == 2 ? amount : 0f));
            else if (mode == GizmoMode.Rotate) Edit(_selected.ToList(), s => { if (axis == 0) s.RotationX = Wrap(s.RotationX + amount); else if (axis == 1) s.RotationY = Wrap(s.RotationY + amount); else s.RotationZ = Wrap(s.RotationZ + amount); }, "girar");
            else Edit(_selected.ToList(), s => { if (axis == 0) s.ScaleX = Mathf.Clamp(s.ScaleX + amount, .01f, 64f); else if (axis == 1) s.ScaleY = Mathf.Clamp(s.ScaleY + amount, .01f, 64f); else s.ScaleZ = Mathf.Clamp(s.ScaleZ + amount, .01f, 64f); }, "tamanho");
        }

        private static float Wrap(float degrees) => Mathf.Repeat(degrees + 180f, 360f) - 180f;

        public void EndDragForTesting(int axis, float amount) { _dragAxis = axis; _dragAmount = amount; EndDrag(); }
        // ---- magnetic snap: while moving, the selection is pulled to the corners, edge middles and face centres of its neighbours, or made to touch a face ----
        public bool SnapMagnet { get; set; } = true;
        public float SnapRadius { get; set; } = .3f;
        private Vector3? _snapTarget;
        public Vector3? SnapPoint => _snapTarget;

        /// <summary>An object's box in game space (z is mirrored), as the shared align and snap rules take it.</summary>
        public static LifeVerse.Shared.Building.Bounds ToGameBounds(UnityEngine.Bounds b) => new LifeVerse.Shared.Building.Bounds(new System.Numerics.Vector3(b.min.x, b.min.y, -b.max.z), new System.Numerics.Vector3(b.max.x, b.max.y, -b.min.z));

        /// <summary>The amount (metres along the game axis) a drag of <paramref name="amount"/> would snap to, or null when nothing is close. Targets are the objects near the selection that are not part of it.</summary>
        public float? SnapAmount(int axis, float amount)
        {
            if (_selected.Count == 0 || Objects == null) return null; var all = SelectionBounds(); var game = ToGameBounds(all); var centre = all.center; var targets = new List<LifeVerse.Shared.Building.Bounds>();
            foreach (var id in Objects.Ids) { if (_selected.Contains(id) || targets.Count >= 120 || !BoundsOf(id, out var b)) continue; if ((b.center - centre).sqrMagnitude < 40f * 40f) targets.Add(ToGameBounds(b)); }
            if (targets.Count == 0) return null;
            var settings = new LifeVerse.Shared.Building.SnapSettings(true, true, true, true, SnapRadius); var half = (game.Max - game.Min) / 2f; var axisVector = axis == 0 ? System.Numerics.Vector3.UnitX : axis == 1 ? System.Numerics.Vector3.UnitY : System.Numerics.Vector3.UnitZ;
            var dragged = LifeVerse.Shared.Building.SnapMath.Features(game.Center, System.Numerics.Quaternion.Identity, half, settings); var points = new List<LifeVerse.Shared.Building.SnapPoint>();
            foreach (var t in targets) points.AddRange(LifeVerse.Shared.Building.SnapMath.Features(t.Center, System.Numerics.Quaternion.Identity, (t.Max - t.Min) / 2f, settings));
            var along = LifeVerse.Shared.Building.SnapMath.AlongAxis(dragged, points, axisVector, amount, SnapRadius); var contact = LifeVerse.Shared.Building.SnapMath.Contact(game, targets, axis, amount, SnapRadius);
            LifeVerse.Shared.Building.SnapResult? best = along; if (contact is { } c && (best == null || Mathf.Abs(c.Amount - amount) < Mathf.Abs(best.Value.Amount - amount))) best = contact; if (best == null) return null;
            _snapTarget = new Vector3(best.Value.Target.X, best.Value.Target.Y, best.Value.Target.Z); return best.Value.Amount;
        }

        // ---- showing a drag to the others as it happens (nothing is saved until the drag is let go) ----
        private float _liveAt; public int LiveSent { get; private set; } public int LiveSeen { get; private set; }

        private void SendLiveIfDue()
        {
            if (Time.realtimeSinceStartup - _liveAt < .1f || _client() == null) return; _liveAt = Time.realtimeSinceStartup; var poses = new List<LifeVerse.Shared.Building.LivePose>();
            foreach (var id in _selected) { var go = Objects?.ViewOf(id); var s = StateOf(id); if (go == null || s == null) continue; var game = GameSpace.ToGame(go.transform.position); var turn = GameSpace.QuaternionToGame(go.transform.rotation).eulerAngles; var scale = go.transform.localScale; var rot = _dragRotation.TryGetValue(id, out var r0) && Mode == GizmoMode.Rotate ? Preview(r0) : new Vector3(s.RotationX, s.RotationY, s.RotationZ); poses.Add(new LifeVerse.Shared.Building.LivePose(id, game.x, game.y, game.z, rot.x, rot.y, rot.z, scale.x, scale.y, scale.z)); }
            if (poses.Count == 0) return; LiveSent++; _client().SendBuild(JsonSerializer.Serialize(new { operation = "live", poses = poses.Take(LifeVerse.Shared.Building.LivePoses.MaxObjects) }, BuildCommands.Web));
        }

        private Vector3 Preview(Vector3 start) { start[_dragAxis] += _dragAmount; return start; }

        private static bool TryCase(JsonElement element, string name, out JsonElement value)
        {
            foreach (var property in element.EnumerateObject()) if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; }
            value = default; return false;
        }

        private void ShowLive(JsonElement root)
        {
            if (!root.TryGetProperty("userId", out var u) || !Guid.TryParse(u.GetString(), out var user) || user == Me || !root.TryGetProperty("poses", out var poses) || poses.ValueKind != JsonValueKind.Array) return;
            foreach (var p in poses.EnumerateArray())
            {
                if (!TryCase(p, "id", out var idElement) || !Guid.TryParse(idElement.GetString(), out var id)) continue; var go = Objects?.ViewOf(id); if (go == null) continue;   // the island writes these with its own capitals: either spelling is read
                float N(string name, float d = 0) => TryCase(p, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out var n) && !float.IsNaN(n) && !float.IsInfinity(n) ? n : d;
                go.transform.position = GameSpace.ToUnity(N("positionX"), N("positionY"), N("positionZ")); go.transform.rotation = GameSpace.RotationToUnity(N("rotationX"), N("rotationY"), N("rotationZ")); go.transform.localScale = new Vector3(Mathf.Max(N("scaleX", 1), .001f), Mathf.Max(N("scaleY", 1), .001f), Mathf.Max(N("scaleZ", 1), .001f)); LiveSeen++;
            }
        }

        /// <summary>Starts listening to the island even outside the build mode, so other people's drags are seen.</summary>
        public void Attach() => Hook();
        // ---- the picture ----

        private void Draw(MeshGenerationContext mgc)
        {
            if (!Active || _camera() == null || _selected.Count == 0) return; var painter = mgc.painter2D; var camera = _camera();
            painter.lineWidth = 1.6f; painter.strokeColor = new Color(.07f, .95f, .88f, .95f);
            foreach (var id in _selected)
            {
                if (!BoundsOf(id, out var b)) continue; var corners = new Vector2[8]; var visible = true;
                for (var i = 0; i < 8; i++) { var p = new Vector3(i % 2 == 0 ? b.min.x : b.max.x, i / 2 % 2 == 0 ? b.min.y : b.max.y, i / 4 == 0 ? b.min.z : b.max.z); if (Vector3.Dot(camera.transform.forward, p - camera.transform.position) <= 0f) visible = false; corners[i] = ToPanel(p); }
                if (!visible) continue; painter.BeginPath();
                foreach (var e in new[] { 0, 1, 0, 2, 0, 4, 3, 1, 3, 2, 3, 7, 5, 1, 5, 4, 5, 7, 6, 2, 6, 4, 6, 7 }.Select((v, k) => new { v, k }).Where(x => x.k % 2 == 0)) { painter.MoveTo(corners[e.v]); painter.LineTo(corners[new[] { 0, 1, 0, 2, 0, 4, 3, 1, 3, 2, 3, 7, 5, 1, 5, 4, 5, 7, 6, 2, 6, 4, 6, 7 }[e.k + 1]]); }
                painter.Stroke();
            }
            if (_snapTarget is Vector3 snap && Vector3.Dot(camera.transform.forward, GameSpace.ToUnity(snap) - camera.transform.position) > 0f) { painter.lineWidth = 2.4f; painter.strokeColor = new Color(1f, .85f, .2f); painter.BeginPath(); painter.Arc(ToPanel(GameSpace.ToUnity(snap)), 9f, 0f, 360f); painter.Stroke(); }
            painter.lineWidth = 3.2f;
            foreach (var h in _handles) { painter.strokeColor = h.Colour; painter.BeginPath(); painter.MoveTo(h.From); painter.LineTo(h.To); painter.Stroke(); painter.fillColor = h.Colour; painter.BeginPath(); if (Mode == GizmoMode.Scale) { painter.MoveTo(h.To + new Vector2(-6, -6)); painter.LineTo(h.To + new Vector2(6, -6)); painter.LineTo(h.To + new Vector2(6, 6)); painter.LineTo(h.To + new Vector2(-6, 6)); painter.ClosePath(); } else if (Mode == GizmoMode.Rotate) { painter.MoveTo(h.To + new Vector2(0, -8)); painter.LineTo(h.To + new Vector2(8, 0)); painter.LineTo(h.To + new Vector2(0, 8)); painter.LineTo(h.To + new Vector2(-8, 0)); painter.ClosePath(); } else painter.Arc(h.To, 6f, 0f, 360f); painter.Fill(); }
        }

        public IReadOnlyList<Vector2> HandleTips => _handles.Select(h => h.To).ToList();
    }
}
