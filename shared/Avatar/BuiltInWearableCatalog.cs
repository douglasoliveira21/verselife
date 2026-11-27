namespace LifeVerse.Shared.Avatar;

/// <summary>
/// The wardrobe of the man of LifeVerse. For now it holds only the "bare" choices (wearing nothing), so he starts as the model is; items that come with the game are added by a generated partial (Generated), each one a file in assets/wearables.
/// </summary>
public static partial class BuiltInWearableCatalog
{
    public static readonly string[] Man = [AvatarRules.DefaultMan];

    /// <summary>The choices of wearing nothing in the slots that must be filled.</summary>
    private static readonly WearableItem[] Bare =
    [
        new("top", "bare_top", "Sem camiseta", Bodies: Man), new("bottom", "bare_bottom", "Sem calça", Bodies: Man), new("shoes", "bare_shoes", "Descalço", Bodies: Man),
        new("jacket", "none", "Sem casaco", Bodies: Man), new("hat", "none", "Sem chapéu", Bodies: Man), new("glasses", "none", "Sem óculos", Bodies: Man), new("hair", "none", "Careca", Bodies: Man), new("gloves", "none", "Sem luvas", Bodies: Man)
    ];

    /// <summary>What a generated list adds to the choices above.</summary>
    static partial void Generated(List<WearableItem> items);

    public static readonly IReadOnlyList<WearableItem> Items = Build();

    private static IReadOnlyList<WearableItem> Build() { var items = new List<WearableItem>(Bare); Generated(items); return items; }
}
