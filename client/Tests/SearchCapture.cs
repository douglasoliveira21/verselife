using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>Windowed run that opens the search window over the world and saves screenshots to LIFEVERSE_CAPTURE_PATH.</summary>
public partial class SearchCapture : Node
{
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    private static readonly Guid Person = Guid.NewGuid(), Group = Guid.NewGuid(), Ev = Guid.NewGuid(), Home = Guid.NewGuid(), Prod = Guid.NewGuid(), Shop = Guid.NewGuid();
    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.AddHours(3);
    private static string Hit(string kind, string label, string title, string sub, string description, string category) =>
        JsonSerializer.Serialize(new { kind, kindLabel = label, id = kind == "person" ? Person : kind == "group" ? Group : kind == "event" ? Ev : kind == "product" ? Prod : kind == "store" ? Shop : Guid.NewGuid(), title, subtitle = sub, description, category, thumbnailUrl = (string?)null, islandId = Home, x = 120f, z = 80f, score = 1.0, popularity = 5.0, favorite = false, startsAt = kind == "event" ? Start : (DateTimeOffset?)null, endsAt = kind == "event" ? Start.AddHours(2) : (DateTimeOffset?)null, priceCoins = kind == "event" ? 0L : kind == "product" ? 250L : (long?)null, rating = "livre", rights = kind == "product" ? 3 : (int?)null });

    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world); await Frames(40);
            if (world.FindChild("Entrar_em_uma_ilha", true, false) is Control join) join.Visible = false;
            var window = (SearchWindow)world.FindChild("SearchWindow", true, false); var path = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH");
            SearchWindow.HistoryPath = "user://capture_search_history.json";
            var down = false;
            var items = new[] { Hit("person", "Pessoa", "Praia Silva", "@praiasilva", "Exploradora e fotógrafa.", ""), Hit("island", "Ilha", "Praia Azul", "por Luna", "Uma praia tropical para relaxar, dançar e conhecer gente.", "social"), Hit("island", "Ilha", "Praia da Música", "por Caio", "Shows ao vivo todo fim de semana.", "musica"), Hit("event", "Evento", "Festa na Praia", "Hoje, 21:00 · Praia Azul", "Música, dança e fogueira.", "festa"), Hit("group", "Grupo", "Amigos da Praia", "128 membros", "Quem ama praia no LifeVerse.", "social"), Hit("product", "Produto", "Prancha Surf", "VC$ 250", "Prancha com animação de surfe.", "esportes"), Hit("store", "Loja", "Loja Praia Shop", "por Marina", "Moda praia e acessórios.", "moda") };
            window.Transport = (method, route, body) =>
            {
                if (down) return (0, "");
                if (route.StartsWith($"/api/v1/users/{Person}/profile/full")) return (200, JsonSerializer.Serialize(new { id = Person, username = "praiasilva", displayName = "Praia Silva", privateProfile = false, joinedAt = "2024-03-15T10:00:00Z", presence = "online", bio = "Exploradora, fotógrafa e amante de mundos virtuais.", hasPhoto = false, groups = new[] { new { name = "Clube do Mar" }, new { name = "Builders Hub" } } }));
                if (route.StartsWith($"/api/v1/discovery/islands/{Home}/info")) return (200, JsonSerializer.Serialize(new { id = Home, name = "Praia Azul", category = "social", contentRating = "livre", language = "pt-BR", owner = new { id = Person, username = "luna", displayName = "Luna Valente" }, online = true, visitors = 12, maxPlayers = 64, access = "open", canEnter = true, isOwner = false, allowsGuestEvents = false }));
                if (route.StartsWith($"/api/v1/discovery/events/{Ev}")) return (200, JsonSerializer.Serialize(new { id = Ev, name = "Festa na Praia", description = "Música, dança e fogueira à beira-mar. Traga os amigos!", category = "festa", categoryLabel = "Festa", startsAt = Start, endsAt = Start.AddHours(2), status = "scheduled", capacity = 100, going = 37, full = false, priceCoins = 0, contentRating = "livre", island = new { id = Home, name = "Praia Azul" }, organizer = new { id = Person, username = "luna", displayName = "Luna Valente" }, joined = false, inCalendar = false, canManage = false, canJoin = true }));
                if (route.StartsWith($"/api/v1/discovery/items/{Prod}")) return (200, JsonSerializer.Serialize(new { id = Prod, name = "Prancha Surf", description = "Prancha de surfe com animação de remada e manobras.", category = "esportes", priceCoins = 250, status = "active", creator = new { id = Person, username = "caio", displayName = "Caio Rocha" }, seller = new { kind = "user", id = Person, name = "Marina Costa" }, rights = new { copy = true, modify = true, transfer = false, resell = false }, nextRights = new { copy = false, modify = false, transfer = true, resell = false }, allowGift = true, resale = false, asset = new { category = "models", mimeType = "model/gltf-binary", byteSize = 184320, version = 2 }, owned = false, isOwn = false, available = true, canBuy = true, store = new { id = Shop, name = "Loja Praia Shop", kind = "virtual" } }));
                if (route.StartsWith($"/api/v1/discovery/stores/{Shop}")) return (200, JsonSerializer.Serialize(new { id = Shop, kind = "virtual", name = "Loja Praia Shop", description = "Moda praia e acessórios para todos os estilos.", category = "moda", categoryLabel = "Moda", owner = new { id = Person, username = "marina", displayName = "Marina Costa" }, island = (object?)null, productCount = 14, featured = new[] { new { id = Prod, name = "Prancha Surf", priceCoins = 250, category = "esportes" }, new { id = Guid.NewGuid(), name = "Biquíni Coral", priceCoins = 90, category = "roupas" }, new { id = Guid.NewGuid(), name = "Chapéu de Palha", priceCoins = 40, category = "roupas" } }, isOwner = false, open = true, ownerId = Person }));
                if (route.StartsWith("/api/v1/social/relations")) return (200, "{\"" + Person + "\":\"none\"}");
                if (route.StartsWith($"/api/v1/groups/{Group}")) return (200, JsonSerializer.Serialize(new { card = new { id = Group, name = "Amigos da Praia", description = "Quem ama praia no LifeVerse.", members = 128, memberLimit = 500, joinPolicy = "open", joinFee = 100, isMember = false, requestPending = false, invited = false, banned = false, category = "social", language = "pt-BR", contentRating = "livre" }, leaderName = "luna", createdAt = "2025-01-02T00:00:00Z" }));
                if (route.StartsWith("/api/v1/search?")) { var kinds = route.Contains("types=") ? Uri.UnescapeDataString(route.Split("types=")[1].Split('&')[0]).Split(',') : null; var shown = items.Where(i => kinds is null || kinds.Any(k => i.Contains($"\"kind\":\"{k}\""))).ToArray(); return (200, $"{{\"total\":{shown.Length},\"page\":1,\"pageSize\":50,\"items\":[{string.Join(",", shown)}],\"counts\":{{\"person\":1,\"island\":9,\"event\":1,\"group\":1,\"product\":1,\"store\":1}},\"categories\":{{\"social\":2,\"musica\":1}},\"sort\":\"relevance\"}}"); }
                if (route.StartsWith("/api/v1/search/popular")) return (200, $"[{items[1]},{items[2]}]");
                if (route.StartsWith("/api/v1/search/recent")) return (200, $"[{Hit("poi", "Ponto", "Farol Novo", "Praia Azul", "", "")}]");
                if (route.StartsWith("/api/v1/search/recommended")) return (200, $"[{items[3]}]");
                if (route.StartsWith("/api/v1/search/interests")) return (200, "[\"musica\",\"praia\"]");
                return (404, "{}");
            };
            window.Open(); await Frames(10); Save(path, "inicial");
            window.SetQueryForTesting("praia"); window.Search(); await Frames(6); window.SelectForTesting(1); await Frames(6); Save(path, "tudo");
            window.SelectTabForTesting(1); await Frames(6); window.SelectForTesting(0); await Frames(8); Save(path, "pessoas");
            window.SelectTabForTesting(4); await Frames(6); window.SelectForTesting(0); await Frames(8); Save(path, "grupos");
            window.SelectTabForTesting(2); await Frames(6); window.SelectForTesting(1); await Frames(8); Save(path, "lugares");
            window.SelectTabForTesting(3); await Frames(6); window.SelectForTesting(0); await Frames(8); Save(path, "eventos");
            window.SelectTabForTesting(5); await Frames(6); window.SelectForTesting(0); await Frames(8); Save(path, "itens");
            window.SelectTabForTesting(6); await Frames(6); window.SelectForTesting(0); await Frames(8); Save(path, "lojas");
            window.CurrentIsland = () => Home; window.OpenEventFormForTesting(); await Frames(8); Save(path, "evento-form");
            window.WindowForTesting.Size = new Vector2(520, 560); await Frames(6); Save(path, "estreito"); window.SelectForTesting(1); await Frames(6); Save(path, "estreito-detalhes");
            window.WindowForTesting.Size = new Vector2(900, 600); down = true; window.SetQueryForTesting("lago"); window.Search(); await Frames(6); Save(path, "erro");
            GD.Print("event=search_capture result=passed"); GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=search_capture result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private void Save(string path, string name) { if (!string.IsNullOrWhiteSpace(path)) GetViewport().GetTexture().GetImage().SavePng($"{path}-{name}.png"); GD.Print($"event=search_capture name={name}"); }
}
