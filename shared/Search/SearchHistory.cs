using System.Text.Json;

namespace LifeVerse.Shared.Search;

/// <summary>
/// What a person searched for lately, kept on their own computer only (the server never sees this list). Newest first, the same search is not listed twice
/// (case, accents and punctuation do not matter), at most <see cref="Capacity"/> lines, nothing shorter than two letters.
/// </summary>
public sealed class SearchHistory
{
    public const int Capacity = 30, MaxLength = 100;
    private readonly List<string> _items = [];

    public IReadOnlyList<string> Items => _items;

    /// <summary>Puts a search at the top. Returns false when it is too short to keep.</summary>
    public bool Add(string? query)
    {
        var shown = (query ?? string.Empty).Trim(); if (shown.Length > MaxLength) shown = shown[..MaxLength].TrimEnd();
        var key = SearchText.Normalize(shown); if (key.Length < 2) return false;
        _items.RemoveAll(i => SearchText.Normalize(i) == key); _items.Insert(0, shown);
        if (_items.Count > Capacity) _items.RemoveRange(Capacity, _items.Count - Capacity);
        return true;
    }

    public bool Remove(string? query) { var key = SearchText.Normalize(query); return key.Length > 0 && _items.RemoveAll(i => SearchText.Normalize(i) == key) > 0; }

    public void Clear() => _items.Clear();

    /// <summary>The past searches that start with (or have a word starting with) what is being typed, newest first; with nothing typed, the latest ones.</summary>
    public IReadOnlyList<string> Matching(string? typed, int take = 5)
    {
        var terms = SearchText.Terms(typed);
        return [.. _items.Where(i => terms.Length == 0 || SearchText.MatchesAll(SearchText.Normalize(i), terms)).Take(Math.Clamp(take, 1, Capacity))];
    }

    public string ToJson() => JsonSerializer.Serialize(_items);

    /// <summary>Reads a saved list; anything that is not a list of texts gives an empty history (a damaged file never stops the game).</summary>
    public static SearchHistory FromJson(string? json)
    {
        var history = new SearchHistory();
        try { if (!string.IsNullOrWhiteSpace(json)) foreach (var item in (JsonSerializer.Deserialize<List<string?>>(json) ?? []).AsEnumerable().Reverse()) history.Add(item); }
        catch (JsonException) { history._items.Clear(); }
        return history;
    }

    public static SearchHistory Load(string path)
    {
        try { return File.Exists(path) ? FromJson(File.ReadAllText(path)) : new SearchHistory(); } catch (IOException) { return new SearchHistory(); } catch (UnauthorizedAccessException) { return new SearchHistory(); }
    }

    public void Save(string path)
    {
        try { var folder = Path.GetDirectoryName(path); if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder); File.WriteAllText(path, ToJson()); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
