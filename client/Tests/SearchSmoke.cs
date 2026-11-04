using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>The search window against a pretend server: searching, filters, paging, hints and history, favorites, reports, interests, and going to a result.</summary>
public partial class SearchSmoke : Node
{
    private readonly List<string> _calls = [];
    private readonly Guid _here = Guid.NewGuid(), _there = Guid.NewGuid(), _parcel = Guid.NewGuid(), _person = Guid.NewGuid(), _plot = Guid.NewGuid();
    private readonly List<string> _teleports = [];
    private Guid _joined;
    private bool _favoritesFull;
    private List<string> _interests = ["musica"];

    private string Hit(string kind, Guid id, string title, Guid? island, float? x, float? z, bool favorite = false, string category = "social") =>
        JsonSerializer.Serialize(new { kind, kindLabel = kind, id, title, subtitle = "sub " + title, description = "desc " + title, category, thumbnailUrl = (string?)null, islandId = island, x, z, score = 1.0, popularity = 5.0, favorite });

    private (int, string) Server(string method, string path, string? body)
    {
        _calls.Add(method + " " + path);
        var items = new[] { Hit("island", _here, "Praia Grande", _here, 10, 20), Hit("island", _there, "Praia Distante", _there, 30, 40, category: "negocios"), Hit("person", _person, "Praia Pessoa", null, null, null), Hit("parcel", _parcel, "Lote da Praia", _here, 5, 6) };
        if (method == "GET" && path.StartsWith("/api/v1/search?")) return (200, $"{{\"total\":45,\"page\":{(path.Contains("page=2") ? 2 : 1)},\"pageSize\":20,\"items\":[{string.Join(",", items)}],\"counts\":{{\"island\":2,\"person\":1,\"parcel\":1}},\"categories\":{{\"social\":3,\"negocios\":1}},\"sort\":\"relevance\"}}");
        if (method == "GET" && path.StartsWith("/api/v1/search/suggest")) return (200, "[{\"text\":\"Praia Grande\",\"kind\":\"island\"},{\"text\":\"Praia Distante\",\"kind\":\"island\"}]");
        if (method == "GET" && path.StartsWith("/api/v1/search/popular")) return (200, $"[{items[0]},{items[1]}]");
        if (method == "GET" && path.StartsWith("/api/v1/search/recent")) return (200, $"[{items[1]},{Hit("poi", _plot, "Farol Novo", _here, 1, 2)}]");
        if (method == "GET" && path.StartsWith("/api/v1/search/favorites?")) return (200, $"{{\"total\":1,\"page\":1,\"pageSize\":20,\"items\":[{Hit("island", _here, "Praia Grande", _here, 10, 20, true)}],\"counts\":{{}},\"categories\":{{}}}}");
        if (path.StartsWith("/api/v1/search/favorites/")) return method == "PUT" && _favoritesFull ? (409, "{}") : (204, string.Empty);
        if (method == "GET" && path == "/api/v1/search/interests") return (200, JsonSerializer.Serialize(_interests));
        if (method == "PUT" && path == "/api/v1/search/interests") { var list = JsonDocument.Parse(body!).RootElement.GetProperty("interests").EnumerateArray().Select(e => e.GetString()!.ToLowerInvariant()); _interests = [.. list]; return (200, JsonSerializer.Serialize(_interests)); }
        if (method == "GET" && path.StartsWith("/api/v1/search/recommended")) return (200, $"[{Hit("poi", _plot, "Estudio de Musica", _here, 1, 2)}]");
        if (method == "POST" && path.StartsWith("/api/v1/search/report/")) return (202, string.Empty);
        if (method == "POST" && path == "/api/v1/search/events") return (204, string.Empty);
        return (404, "{}");
    }

    public override void _Ready()
    {
        try
        {
            SearchWindow.HistoryPath = "user://test_search_history.json"; DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SearchWindow.HistoryPath));
            var window = new SearchWindow { Name = "SearchWindow", Transport = Server, CurrentIsland = () => _here, JoinIsland = id => _joined = id, TeleportToPoint = (x, z) => _teleports.Add($"{x:0},{z:0}"), TeleportToParcel = id => { if (id == _parcel) { _teleports.Add("parcel"); return true; } return false; } };
            AddChild(window);

            window.Open();
            Require(window.Visible && window.Hits.Count == 3 && window.Hits.Select(h => h.Title).SequenceEqual(["Praia Grande", "Praia Distante", "Farol Novo"]), $"with nothing typed, popular places then new ones without repeats: {string.Join(",", window.Hits.Select(h => h.Title))}");

            window.SetQueryForTesting("x"); window.Search(); Require(window.NoteForTesting.Contains("pelo menos 2"), "one letter is not a search"); Require(!_calls.Any(c => c.Contains("q=x")), "nothing was sent for it");
            window.SetQueryForTesting("praia"); window.Search();
            Require(_calls.Contains("GET /api/v1/search?q=praia&page=1&pageSize=20&sort=relevance"), "the search path: " + _calls.Last(c => c.Contains("search?")));
            Require(window.Hits.Count == 4 && window.NoteForTesting.StartsWith("45 resultado"), "results are listed with the total");
            Require(window.CategoriesForTesting.SequenceEqual(["Todas as categorias", "social", "negocios"]), "the categories found become the category filter");
            Require(window.PagerVisibleForTesting, "45 results need paging"); window.NextPageForTesting(); Require(_calls.Last().Contains("page=2") && _calls.Last().Contains("q=praia"), "the next page is the same search: " + _calls.Last());

            window.SetFiltersForTesting(1, 1); window.Search(); Require(_calls.Last().Contains("types=island") && _calls.Last().Contains("sort=popularity"), "kind and order are sent: " + _calls.Last());
            window.SetCategoryForTesting("negocios"); window.Search(); Require(_calls.Last().Contains("category=negocios"), "category is sent: " + _calls.Last());
            window.SetFiltersForTesting(0, 0); window.SetCategoryForTesting("Todas as categorias"); window.Search();

            // hints: the search made before comes back first, then what the server suggests
            window.SetQueryForTesting("pra"); window.ShowHints(["Praia Grande", "Praia Distante"]);
            Require(window.HintsForTesting.SequenceEqual(["praia", "Praia Grande", "Praia Distante"]), "hints mix history and suggestions: " + string.Join("|", window.HintsForTesting));
            window.ClearHistory(); window.ShowHints(); Require(window.HintsForTesting.Count == 0, "the history can be cleared");

            // preview and going there
            window.SetQueryForTesting("praia"); window.Search();
            window.SelectForTesting(0); Require(window.PreviewTitleForTesting == "Praia Grande" && window.PreviewWhereForTesting.StartsWith("Nesta ilha") && window.GoTextForTesting == "Teleportar até lá", "a result on this island: " + window.PreviewWhereForTesting);
            window.GoToResult(); Require(_teleports.SequenceEqual(["10,20"]) && _joined == Guid.Empty, "teleports to the spot without leaving the island");
            window.SelectForTesting(3); window.GoToResult(); Require(_teleports.Last() == "parcel", "a plot on this island goes through the plot teleport");
            window.SelectForTesting(1); Require(window.PreviewWhereForTesting.StartsWith("Em outra ilha") && window.GoTextForTesting == "Ir para a ilha", "a result on another island: " + window.PreviewWhereForTesting);
            window.GoToResult(); Require(_joined == _there && SearchTravel.HasPending, "joins the island and keeps the spot for when it arrives");
            Require(SearchTravel.TakeFor(Guid.NewGuid()) is null && !SearchTravel.HasPending, "arriving somewhere else forgets the spot");
            window.GoToResult(); Require(SearchTravel.TakeFor(_there) is { X: 30, Z: 40 }, "arriving at the island gives the spot back");
            window.SelectForTesting(2); Require(!window.GoEnabledForTesting && window.GoTextForTesting == "Sem lugar para visitar", "a person has no place to go");
            Require(_calls.Any(c => c == "POST /api/v1/search/events"), "opening and going are counted");

            // favorites
            window.SelectForTesting(0); window.FavoriteForTesting();
            Require(_calls.Last() == $"PUT /api/v1/search/favorites/island/{_here}" && window.FavoriteTextForTesting == "Remover dos favoritos" && window.RowTextForTesting(0).StartsWith("★ "), "favoriting marks the row");
            window.FavoriteForTesting(); Require(_calls.Last().StartsWith("DELETE ") && window.FavoriteTextForTesting == "Favoritar", "and unmarks it");
            _favoritesFull = true; window.FavoriteForTesting(); Require(window.NoteForTesting.Contains("cheia"), "a full list is explained"); _favoritesFull = false;

            // report
            window.SelectForTesting(1); window.ReportForTesting("ab"); Require(window.NoteForTesting.Contains("motivo"), "a report needs a reason");
            window.ReportForTesting("conteudo ofensivo"); Require(_calls.Last() == $"POST /api/v1/search/report/island/{_there}" && window.NoteForTesting.StartsWith("Denúncia enviada"), "the report is sent: " + window.NoteForTesting);

            // tabs
            window.SelectTabForTesting(1); Require(window.TabForTesting == 1 && window.Hits.Count == 1 && window.NoteForTesting == "1 favorito(s).", "favorites tab");
            window.SelectForTesting(0); window.FavoriteForTesting(); Require(window.Hits.Count == 1 && _calls.Any(c => c.StartsWith("DELETE ")), "removing a favorite reloads the list");
            window.SelectTabForTesting(2); Require(window.InterestsForTesting == "musica" && window.Hits.Count == 1 && window.Hits[0].Title == "Estudio de Musica", "recommendations and interests");
            window.SetInterestsForTesting("Arte, Pesca"); Require(window.InterestsForTesting == "arte, pesca" && _calls.Any(c => c == "PUT /api/v1/search/interests"), "interests are saved and cleaned by the server");

            // history file survives a reload
            var again = new SearchWindow { Name = "SearchWindow2", Transport = Server }; AddChild(again); again.SetQueryForTesting("pr"); again.ShowHints();
            Require(again.HintsForTesting.SequenceEqual(["praia"]), "the history on disk has only what was searched after it was cleared: " + string.Join("|", again.HintsForTesting));
            window.SelectTabForTesting(0); window.SetQueryForTesting("lago"); window.Search(); var third = new SearchWindow { Name = "SearchWindow3", Transport = Server }; AddChild(third); third.SetQueryForTesting("la"); third.ShowHints();
            Require(third.HintsForTesting.SequenceEqual(["lago"]), "searches are remembered between sessions");

            // a failing server is told, not hidden
            var broken = new SearchWindow { Name = "Broken", Transport = (_, _, _) => (0, string.Empty) }; AddChild(broken); broken.SetQueryForTesting("praia"); broken.Search();
            Require(broken.NoteForTesting == "Sem conexão com o servidor." && broken.Hits.Count == 0, "no connection is said plainly");

            // the answers are read defensively
            Require(SearchApi.ParsePage("not json").Total == 0 && SearchApi.ParseList("{}").Count == 0 && SearchApi.ParseSuggestions("[1,2]").Count == 0, "bad answers are empty");
            Require(!SearchApi.IsLoadableImageUrl("file:///c:/x.png") && !SearchApi.IsLoadableImageUrl("https://u:p@host/x.png") && SearchApi.IsLoadableImageUrl("https://cdn.exemplo.com/x.png"), "only plain http(s) thumbnails");

            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SearchWindow.HistoryPath));
            GD.Print("event=search_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=search_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
