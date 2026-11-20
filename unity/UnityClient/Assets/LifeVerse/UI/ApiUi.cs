using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>What the windows that talk to the platform need: the interface, the signed-in API client, the notices, and how to go to another island.</summary>
    public sealed class ApiContext
    {
        public UiRoot Ui; public LifeVerse.Unity.World.IslandEntryClient Api; public NotificationCenter Notices;
        public Func<Guid> CurrentIsland = () => Guid.Empty;
        /// <summary>Leaves the island and enters another one.</summary>
        /// <summary>Travels to a spot of an island (meters from its centre; null: where the island spawns people).</summary>
        public Action<Guid, float?, float?> TravelTo;
        public Action<Guid> EnterIsland = _ => { };
    }

    /// <summary>One element of a JSON list the API sent, with tolerant readers (the API is camelCase; a field that is not there gives the fallback).</summary>
    public readonly struct Row
    {
        private readonly JsonElement _e;
        public Row(JsonElement element) { _e = element; }
        public bool Has(string name) => _e.ValueKind == JsonValueKind.Object && _e.TryGetProperty(name, out _);
        public string Text(string name, string fallback = "") => _e.ValueKind == JsonValueKind.Object && _e.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : v.ValueKind == JsonValueKind.Null ? fallback : v.ToString() : fallback;
        public string First(params string[] names) { foreach (var name in names) { var text = Text(name); if (text.Length > 0) return text; } return ""; }
        public long Number(string name, long fallback = 0) => _e.ValueKind == JsonValueKind.Object && _e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : fallback;
        public bool Flag(string name) => _e.ValueKind == JsonValueKind.Object && _e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
        public Guid Id(params string[] names) { foreach (var name in names.Length == 0 ? new[] { "id" } : names) if (Guid.TryParse(Text(name), out var id)) return id; return Guid.Empty; }
        public JsonElement Element => _e;
        /// <summary>A nested object (empty when it is not there).</summary>
        public Row Child(string name) => _e.ValueKind == JsonValueKind.Object && _e.TryGetProperty(name, out var v) ? new Row(v) : new Row(default);
    }

    public static class Json
    {
        /// <summary>The rows of an answer that is a list, or an object holding one (items, data, results, messages, entries). Empty when it is neither.</summary>
        public static List<Row> Rows(string body)
        {
            var rows = new List<Row>();
            try
            {
                using var document = JsonDocument.Parse(body); var root = document.RootElement; JsonElement list = default; var found = false;
                if (root.ValueKind == JsonValueKind.Array) { list = root; found = true; }
                else if (root.ValueKind == JsonValueKind.Object) foreach (var name in new[] { "items", "data", "results", "messages", "entries", "transactions", "islands", "people", "friends", "groups", "requests", "parcels", "gallery", "versions" }) if (root.TryGetProperty(name, out list) && list.ValueKind == JsonValueKind.Array) { found = true; break; }
                if (found) foreach (var item in list.EnumerateArray()) rows.Add(new Row(item.Clone()));
            }
            catch (JsonException) { }
            return rows;
        }

        public static Row One(string body) { try { using var document = JsonDocument.Parse(body); return new Row(document.RootElement.Clone()); } catch (JsonException) { return new Row(default); } }
    }

    /// <summary>Lets a window wait for a call to the API without blocking: the callback runs on the interface thread when the task is done.</summary>
    public static class UiTasks
    {
        public static void Then<T>(this VisualElement host, Task<T> task, Action<T> done)
        {
            IVisualElementScheduledItem item = null;
            item = host.schedule.Execute(() => { if (!task.IsCompleted) return; item.Pause(); if (task.IsFaulted || task.IsCanceled) return; done(task.Result); }).Every(40);
        }

        /// <summary>The words for what the API answered when it was not a success.</summary>
        /// <summary>The generic sentence for the status, but when the API said why (message, detail or title of its answer) that reason is used.</summary>
        public static string Explain(int status, string body)
        {
            if (status >= 400 && status < 500 && status != 401 && !string.IsNullOrEmpty(body))
            {
                var row = Json.One(body); var why = row.First("message", "detail", "error", "title");
                if (why.Length > 0 && why.Length < 240) return why;
            }
            return Explain(status);
        }

        public static string Explain(int status) => status switch
        {
            0 => "Sem resposta da API.", 401 => "Sua sessão acabou. Entre de novo.", 403 => "Você não tem permissão para isso.", 404 => "Não encontrado.", 409 => "O pedido conflita com o que já existe.", 429 => "Muitos pedidos. Espere um pouco.", >= 500 => "A API está com problemas (" + status + ").", _ => "A API recusou o pedido (" + status + ").",
        };
    }
}
