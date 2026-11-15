using System.Globalization;
using System.Text;

namespace LifeVerse.Client.Scripts.Settings;

/// <summary>The 18 categories of the settings window, in the order of the side bar.</summary>
public enum SettingCategory
{
    General, Graphics, Audio, Voice, Controls, Camera, Avatar, Chat, Privacy, Notifications, Inventory, Building, World, Scripts, Network, Interface, Accessibility, Advanced
}

public static class SettingCategories
{
    public static readonly string[] Labels =
    [
        "Geral e conta", "Gráficos e desempenho", "Áudio", "Comunicação por voz", "Controles e movimentação", "Câmera e visualização", "Avatar e aparência", "Chat e social",
        "Privacidade e segurança", "Notificações", "Inventário e conteúdo", "Construção e edição", "Mundo, ilhas e teleportes", "Scripts e interações", "Rede e conectividade",
        "Interface e HUD", "Acessibilidade", "Avançado e diagnóstico"
    ];
    public static string LabelOf(SettingCategory category) => Labels[(int)category];
}

/// <summary>Action is a button that does something once (clear the cache, open a folder); Info is a value that is shown and never changed. Neither is saved in a profile.</summary>
public enum SettingKind { Bool, Int, Float, Choice, Text, Key, Action, Info }

public sealed record SettingChoice(string Id, string Label);

/// <summary>
/// What one setting is: where it lives in the window, how it is shown, what values it accepts and what it starts as. This table is the single source for the
/// window, the search, the validation, the defaults, "Descartar" and the profiles. What a setting <i>does</i> is a separate applier registered in the store.
/// </summary>
public sealed record SettingDefinition(string Id, SettingCategory Category, string Tab, string Label, string Description, SettingKind Kind, object Default)
{
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public double Min { get; init; }
    public double Max { get; init; } = 1;
    /// <summary>Step of a number (0 = free). Values are snapped to it.</summary>
    public double Step { get; init; }
    public string Unit { get; init; } = "";
    public IReadOnlyList<SettingChoice> Choices { get; init; } = [];
    /// <summary>Options that depend on the device (monitors, resolutions): asked when needed. Any short text is accepted as a value, the applier checks it against the device.</summary>
    public Func<IReadOnlyList<SettingChoice>>? ChoiceSource { get; init; }
    public IReadOnlyList<SettingChoice> CurrentChoices => ChoiceSource?.Invoke() ?? Choices;
    public bool RestartRequired { get; init; }
    /// <summary>Changing it by hand turns the quality preset into "Personalizada".</summary>
    public bool PresetControlled { get; init; }
    /// <summary>Why this setting cannot work in this client (shown disabled instead of pretending). Null when it works.</summary>
    public string? Unavailable { get; init; }
    public int MaxLength { get; init; } = 64;
    /// <summary>For an Info: reads the value to show. For an Action: the text of the button.</summary>
    public Func<string>? InfoSource { get; init; }
    public string ButtonText { get; init; } = "Executar";
    /// <summary>For an Action: when set, the person is asked this before it runs.</summary>
    public string? Confirm { get; init; }
    public bool IsValueSetting => Kind is not (SettingKind.Action or SettingKind.Info);
    /// <summary>The card of the page this setting is drawn in, and the column (0 left, 1 right) on wide screens.</summary>
    public string Group { get; init; } = "";
    public int Column { get; init; }

    public bool IsAvailable => Unavailable is null;

    /// <summary>The value this setting accepts for <paramref name="raw"/>: converted to the type, limited to the range, snapped to the step, or the default when it makes no sense.</summary>
    public object Normalize(object? raw)
    {
        try
        {
            switch (Kind)
            {
                case SettingKind.Action:
                case SettingKind.Info:
                    return Default;
                case SettingKind.Bool:
                    return raw switch { bool b => b, string s when bool.TryParse(s, out var parsed) => parsed, long l => l != 0, int i => i != 0, double d => d != 0, _ => (bool)Default };
                case SettingKind.Int:
                {
                    var number = raw switch { null => (double)Convert.ToInt32(Default, CultureInfo.InvariantCulture), string s => double.Parse(s, CultureInfo.InvariantCulture), _ => Convert.ToDouble(raw, CultureInfo.InvariantCulture) };
                    if (!double.IsFinite(number)) return Convert.ToInt32(Default, CultureInfo.InvariantCulture);
                    return (int)Math.Round(Snap(Math.Clamp(number, Min, Max)));
                }
                case SettingKind.Float:
                {
                    var number = raw switch { null => Convert.ToDouble(Default, CultureInfo.InvariantCulture), string s => double.Parse(s, CultureInfo.InvariantCulture), _ => Convert.ToDouble(raw, CultureInfo.InvariantCulture) };
                    if (!double.IsFinite(number)) return Convert.ToSingle(Default, CultureInfo.InvariantCulture);
                    return (float)Snap(Math.Clamp(number, Min, Max));
                }
                case SettingKind.Choice:
                    return raw is string id && (ChoiceSource is not null ? id.Length is >= 1 and <= 32 && !id.Any(char.IsControl) : Choices.Any(c => c.Id == id)) ? id : (string)Default;
                case SettingKind.Text:
                case SettingKind.Key:
                {
                    var text = raw as string ?? (string)Default;
                    var clean = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
                    clean = clean.Length > MaxLength ? clean[..MaxLength] : clean;
                    return Kind == SettingKind.Key && !KeyBindings.IsValid(clean) ? (string)Default : clean;        // a key must be one the engine knows and the game does not keep for itself
                }
            }
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException) { }
        return Default;
    }

    private double Snap(double value) => Step > 0 ? Min + Math.Round((value - Min) / Step) * Step : value;

    /// <summary>Text of the value for the window ("60 FPS", "Alta", "Ligado").</summary>
    public string Display(object value) => Kind switch
    {
        SettingKind.Bool => (bool)value ? "Ligado" : "Desligado",
        SettingKind.Choice => CurrentChoices.FirstOrDefault(c => c.Id == (string)value)?.Label ?? (string)value,
        SettingKind.Float => string.Format(CultureInfo.GetCultureInfo("pt-BR"), "{0:0.##}{1}", value, Unit),
        _ => $"{value}{Unit}"
    };
}

/// <summary>The search of the window: by name, category, tab, description and keywords, ignoring case and accents.</summary>
public static class SettingsSearch
{
    public static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD); var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(char.ToLowerInvariant(c));
        return builder.ToString();
    }

    /// <summary>The settings that match every word of the query, best first (a hit in the name beats the category, the description and the keywords).</summary>
    public static IReadOnlyList<SettingDefinition> Find(IEnumerable<SettingDefinition> catalog, string query)
    {
        var words = Fold(query ?? "").Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [];
        var scored = new List<(SettingDefinition Definition, int Score)>();
        foreach (var definition in catalog)
        {
            var label = Fold(definition.Label); var category = Fold(SettingCategories.LabelOf(definition.Category)); var tab = Fold(definition.Tab);
            var description = Fold(definition.Description); var keywords = Fold(string.Join(' ', definition.Keywords));
            var score = 0; var all = true;
            foreach (var word in words)
            {
                var best = label.StartsWith(word) ? 100 : label.Contains(word) ? 80 : keywords.Contains(word) ? 60 : tab.Contains(word) ? 45 : category.Contains(word) ? 40 : description.Contains(word) ? 20 : 0;
                if (best == 0) { all = false; break; }
                score += best;
            }
            if (all) scored.Add((definition, score));
        }
        return [.. scored.OrderByDescending(x => x.Score).ThenBy(x => x.Definition.Category).ThenBy(x => x.Definition.Label, StringComparer.Ordinal).Select(x => x.Definition)];
    }

    /// <summary>Where the words of the query sit in a text (to highlight them): start and length, ignoring case and accents.</summary>
    public static IReadOnlyList<(int Start, int Length)> Highlights(string text, string query)
    {
        var folded = Fold(text); var found = new List<(int, int)>();
        if (folded.Length != text.Length) return found;     // a text that changes length when folded cannot be mapped back
        foreach (var word in Fold(query ?? "").Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var at = folded.IndexOf(word, StringComparison.Ordinal);
            if (at >= 0) found.Add((at, word.Length));
        }
        return [.. found.OrderBy(x => x.Item1)];
    }
}
