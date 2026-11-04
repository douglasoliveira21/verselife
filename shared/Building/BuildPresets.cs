namespace LifeVerse.Shared.Building;

/// <summary>A ready-made construction offered by the editor ("Predefinidos"): plain package data, placed like one saved in the inventory.</summary>
public sealed record BuildPreset(string Id, string Name, string Description, BuildPackage Package)
{
    public int Parts => Package.Objects.Length;
}

/// <summary>
/// The construction presets: a wall, a floor, a pillar, stairs, a table and a small room. They use only plain shapes (no assets), so anyone can place them;
/// several are linksets with a root and children, so they also show how a linkset behaves.
/// </summary>
public static class BuildPresets
{
    private const string Physics = "{\"collision\":true}";

    public static IReadOnlyList<BuildPreset> All { get; } = Build();

    public static BuildPreset? Find(string id) => All.FirstOrDefault(p => p.Id == id);

    private static SourceObject Part(string shape, string name, string color, float x, float y, float z, float sx, float sy, float sz, Guid? group = null, Guid id = default, Guid? parent = null) =>
        new(shape, name, string.Empty, x, y, z, 0, 0, 0, sx, sy, sz, $"{{\"color\":\"{color}\",\"roughness\":0.8}}", "{}", Physics, "[]", group, id, parent);

    private static Guid Id(int n) => new(n, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]);

    private static List<BuildPreset> Build()
    {
        var list = new List<BuildPreset>();
        void Add(string id, string name, string description, params SourceObject[] parts) => list.Add(new BuildPreset(id, name, description, BuildPackages.Create(name, parts)));

        Add("wall", "Parede", "Uma parede de 4 × 3 m com 20 cm de espessura.", Part("cube", "Parede", "#CFC8BA", 0, 1.5f, 0, 4, 3, .2f));
        Add("floor", "Piso", "Um piso de 4 × 4 m.", Part("cube", "Piso", "#9A9A94", 0, .1f, 0, 4, .2f, 4));
        Add("pillar", "Coluna", "Uma coluna redonda de 3 m.", Part("cylinder", "Coluna", "#D9D4C7", 0, 1.5f, 0, .5f, 3, .5f));

        var stairs = Id(100);
        var steps = new List<SourceObject>();
        for (var i = 0; i < 6; i++) { var height = .3f * (i + 1); steps.Add(Part("cube", $"Degrau {i + 1}", "#A89F91", 0, height / 2, i * .6f, 2, height, .6f, stairs, Id(110 + i), i == 0 ? null : Id(110))); }
        Add("stairs", "Escada", "Seis degraus ligados num só conjunto: o primeiro é a raiz.", [.. steps]);

        var table = Id(200);
        Add("table", "Mesa", "Tampo e quatro pés ligados: mover o tampo leva a mesa toda.",
            Part("cube", "Tampo", "#8B5E3C", 0, .75f, 0, 2, .1f, 1, table, Id(210)),
            Part("cube", "Pé", "#6B452B", -.9f, .35f, -.4f, .1f, .7f, .1f, table, Id(211), Id(210)),
            Part("cube", "Pé", "#6B452B", .9f, .35f, -.4f, .1f, .7f, .1f, table, Id(212), Id(210)),
            Part("cube", "Pé", "#6B452B", -.9f, .35f, .4f, .1f, .7f, .1f, table, Id(213), Id(210)),
            Part("cube", "Pé", "#6B452B", .9f, .35f, .4f, .1f, .7f, .1f, table, Id(214), Id(210)));

        var room = Id(300);
        Add("room", "Cômodo", "Piso e quatro paredes de 3 m ligados: o piso é a raiz.",
            Part("cube", "Piso", "#9A9A94", 0, .1f, 0, 4, .2f, 4, room, Id(310)),
            Part("cube", "Parede", "#CFC8BA", 0, 1.7f, -1.9f, 4, 3, .2f, room, Id(311), Id(310)),
            Part("cube", "Parede", "#CFC8BA", 0, 1.7f, 1.9f, 4, 3, .2f, room, Id(312), Id(310)),
            Part("cube", "Parede", "#CFC8BA", -1.9f, 1.7f, 0, .2f, 3, 3.6f, room, Id(313), Id(310)),
            Part("cube", "Parede", "#CFC8BA", 1.9f, 1.7f, 0, .2f, 3, 3.6f, room, Id(314), Id(310)));
        return list;
    }
}
