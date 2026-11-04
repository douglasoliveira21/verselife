using System.Globalization;
using System.Text;

namespace LifeVerse.Shared.Search;

/// <summary>What can be searched for, in the order they are listed to people.</summary>
public static class SearchKinds
{
    public const string Island = "island", Parcel = "parcel", Person = "person", Group = "group", Store = "store", Product = "product", Event = "event", Place = "poi";
    public static readonly string[] All = [Island, Parcel, Person, Group, Store, Product, Event, Place];
    /// <summary>The kinds that are a place one can go to: the base of "popular places" and "recent places".</summary>
    public static readonly string[] Places = [Island, Parcel, Place];
    public static string Label(string kind) => kind switch { Island => "Ilha", Parcel => "Terreno", Person => "Pessoa", Group => "Grupo", Store => "Loja", Product => "Produto", Event => "Evento", Place => "Ponto de interesse", _ => kind };
}

/// <summary>
/// How text is compared in the search: lower case, without accents, only letters and digits, words separated by one space. Used to build the text that is
/// indexed and to read what the person typed, so both sides always agree; and to score the results (the order of relevance does not depend on the database).
/// </summary>
public static class SearchText
{
    public const int MaxTerms = 8, MaxTermLength = 32, MaxQueryLength = 100;

    /// <summary>"Ação Rápida!" becomes "acao rapida".</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var decomposed = text.Normalize(NormalizationForm.FormD); var sb = new StringBuilder(decomposed.Length); var space = true;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) { sb.Append(char.ToLowerInvariant(c)); space = false; }
            else if (!space) { sb.Append(' '); space = true; }
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>The words of a query: at most eight, each at most 32 letters or digits.</summary>
    public static string[] Terms(string? query)
    {
        var text = Normalize(query is { Length: > MaxQueryLength } ? query[..MaxQueryLength] : query);
        return text.Length == 0 ? [] : [.. text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Length > MaxTermLength ? t[..MaxTermLength] : t).Distinct().Take(MaxTerms)];
    }

    /// <summary>Whether a word of the indexed text starts with the term.</summary>
    public static bool HasWordStartingWith(string normalized, string term) => normalized.StartsWith(term, StringComparison.Ordinal) || normalized.Contains(" " + term, StringComparison.Ordinal);

    /// <summary>Whether every term starts a word of the text.</summary>
    public static bool MatchesAll(string normalized, IReadOnlyCollection<string> terms) => terms.Count > 0 && terms.All(t => HasWordStartingWith(normalized, t));

    /// <summary>The query for PostgreSQL full-text search: every term is a prefix, all of them must be there ("prai & ilh" -> "prai:* &amp; ilh:*").</summary>
    public static string TsQuery(IEnumerable<string> terms) => string.Join(" & ", terms.Select(t => new string([.. t.Where(char.IsAsciiLetterOrDigit)])).Where(t => t.Length > 0).Select(t => t + ":*"));

    /// <summary>
    /// How well a result answers a query, from 0 up: the whole title is the query, the title starts with it, has all its words, some of them, then the
    /// rest of the text; a little more for what many people visit and for what is recent. Plain arithmetic, the same wherever the candidates came from.
    /// </summary>
    public static double Relevance(string title, string text, string query, IReadOnlyCollection<string> terms, double popularity, DateTimeOffset updatedAt, DateTimeOffset now)
    {
        var t = Normalize(title); var q = Normalize(query); double score = 20; // all the terms are in the text, or it would not be a candidate
        if (t == q) score += 80;
        else if (t.StartsWith(q, StringComparison.Ordinal)) score += 60;
        else if (t.Contains(" " + q, StringComparison.Ordinal)) score += 45;
        var inTitle = terms.Count(term => HasWordStartingWith(t, term));
        score += 25.0 * inTitle / Math.Max(1, terms.Count);
        if (terms.Count > 0 && inTitle == terms.Count) score += 10;
        score += Math.Min(15, Math.Log2(1 + Math.Max(0, popularity)) * 3);
        var days = (now - updatedAt).TotalDays; if (days is >= 0 and < 30) score += 5 * (1 - days / 30);
        return Math.Round(score, 3);
    }

    /// <summary>A short piece of text for the results list, cut at a word.</summary>
    public static string Snippet(string? text, int max = 160)
    {
        var clean = new string([.. (text ?? string.Empty).Where(c => !char.IsControl(c) || c == ' ')]).Trim();
        if (clean.Length <= max) return clean;
        var cut = clean[..max]; var space = cut.LastIndexOf(' ');
        return (space > max / 2 ? cut[..space] : cut).TrimEnd() + "…";
    }
}
