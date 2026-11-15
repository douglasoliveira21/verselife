using Godot;
using LifeVerse.Client.Scripts.Settings;

namespace LifeVerse.Client.Tests;

/// <summary>Stage 0 of the settings menu: the catalog rules, search, pending/apply/discard, presets, profiles, import/export, schema migration and the legacy files.</summary>
public partial class SettingsStoreSmoke : Node
{
    private const string Root = "user://smoke_settings";

    private static IReadOnlyList<SettingDefinition> Catalog() =>
    [
        new("graphics.preset", SettingCategory.Graphics, "Geral", "Predefinição gráfica", "Conjunto de efeitos.", SettingKind.Choice, "high") { Choices = [new("low", "Baixa"), new("high", "Alta"), new("custom", "Personalizada")], Keywords = ["qualidade"] },
        new("graphics.msaa", SettingCategory.Graphics, "Qualidade", "Antialiasing", "Suaviza bordas.", SettingKind.Int, 2) { Min = 0, Max = 8, Step = 2, PresetControlled = true },
        new("graphics.shadows", SettingCategory.Graphics, "Qualidade", "Sombras", "Sombras do sol.", SettingKind.Bool, true) { PresetControlled = true },
        new("graphics.vsync", SettingCategory.Graphics, "Geral", "Sincronização vertical (V-Sync)", "Evita imagem rasgada.", SettingKind.Bool, true),
        new("graphics.fps_limit", SettingCategory.Graphics, "Geral", "Limite de FPS", "Quadros por segundo.", SettingKind.Int, 120) { Min = 30, Max = 360, Step = 10, RestartRequired = false },
        new("audio.master", SettingCategory.Audio, "Geral", "Volume geral", "Volume de todos os sons.", SettingKind.Float, 1f) { Min = 0, Max = 1, Step = .05 },
        new("audio.device", SettingCategory.Audio, "Geral", "Saída de áudio", "Dispositivo.", SettingKind.Text, "") { RestartRequired = true },
        new("voice.noise", SettingCategory.Voice, "Microfone", "Redução de ruído", "Não há no cliente.", SettingKind.Bool, false) { Unavailable = "O cliente não tem redução de ruído." },
    ];

    private static SettingsPresetGroup Presets() => new("graphics.preset", "custom", new Dictionary<string, IReadOnlyDictionary<string, object>>
    {
        ["low"] = new Dictionary<string, object> { ["graphics.msaa"] = 0, ["graphics.shadows"] = false },
        ["high"] = new Dictionary<string, object> { ["graphics.msaa"] = 4, ["graphics.shadows"] = true }
    });

    private static void Wipe()
    {
        var absolute = ProjectSettings.GlobalizePath(Root);
        if (!DirAccess.DirExistsAbsolute(absolute)) return;
        foreach (var file in DirAccess.GetFilesAt(absolute)) DirAccess.RemoveAbsolute($"{absolute}/{file}");
    }

    public override void _Ready()
    {
        try
        {
            Wipe();
            // ---- the rules of a setting
            var fps = Catalog().First(d => d.Id == "graphics.fps_limit");
            Require((int)fps.Normalize(9000) == 360 && (int)fps.Normalize(-5) == 30 && (int)fps.Normalize(97) == 100 && (int)fps.Normalize("144") == 140 && (int)fps.Normalize("abc") == 120 && (int)fps.Normalize(null) == 120, "numbers are limited to the range, snapped to the step, and garbage gives the default");
            Require((float)Catalog().First(d => d.Id == "audio.master").Normalize(.33) is > .34f and < .36f && (float)Catalog().First(d => d.Id == "audio.master").Normalize(double.NaN) == 1f, "floats snap to the step and NaN gives the default");
            var preset = Catalog()[0]; Require((string)preset.Normalize("low") == "low" && (string)preset.Normalize("ultra") == "high" && (string)preset.Normalize(5) == "high", "a choice must be one of the options");
            var text = Catalog().First(d => d.Id == "audio.device"); Require((string)text.Normalize("Alto\nfalante\t") == "Altofalante" && ((string)text.Normalize(new string('x', 500))).Length == 64, "text loses control characters and is cut at the limit");
            Require((bool)Catalog()[3].Normalize("false") == false && (bool)Catalog()[3].Normalize(0) == false && (bool)Catalog()[3].Normalize("???") == true, "booleans read from text and numbers");
            Require(fps.Display(120) == "120" && Catalog()[3].Display(true) == "Ligado" && preset.Display("high") == "Alta", "values are shown for people");

            // ---- search
            var found = SettingsSearch.Find(Catalog(), "sincronizacao vertical");
            Require(found.Count == 1 && found[0].Id == "graphics.vsync", "search ignores accents and matches every word");
            Require(SettingsSearch.Find(Catalog(), "qualidade").Any(d => d.Id == "graphics.preset"), "search finds by keyword");
            Require(SettingsSearch.Find(Catalog(), "AUDIO").Select(d => d.Id).Contains("audio.master"), "search finds by category, ignoring case");
            Require(SettingsSearch.Find(Catalog(), "volume").First().Id == "audio.master" && SettingsSearch.Find(Catalog(), "zzzz").Count == 0 && SettingsSearch.Find(Catalog(), "  ").Count == 0, "a hit in the name comes first; nothing matches nonsense or blanks");
            var marks = SettingsSearch.Highlights("Sincronização vertical", "vertical sincron");
            Require(marks.Count == 2 && marks[0] == (0, 7) && marks[1] == (14, 8), "matches are highlighted ignoring accents");

            // ---- pending, apply, discard
            var applied = new List<(string, object)>();
            var store = new SettingsStore(Root, Catalog(), Presets());
            foreach (var d in Catalog()) { var id = d.Id; store.RegisterApplier(id, v => applied.Add((id, v))); }
            Require(store.ActiveProfile == "Padrão" && (int)store.Value("graphics.fps_limit") == 120 && !store.HasPending, "starts with the default profile and the defaults");
            store.Set("graphics.fps_limit", 144); store.Set("audio.master", .5);
            Require(store.HasPending && (int)store.Effective("graphics.fps_limit") == 140 && (int)store.Value("graphics.fps_limit") == 120 && applied.Count == 0, "a change is pending: the window shows it, the game does not use it yet");
            store.Discard(); Require(!store.HasPending && (int)store.Effective("graphics.fps_limit") == 120, "Descartar drops it");
            store.Set("graphics.fps_limit", 60); store.Set("audio.device", "Fones");
            var result = store.Apply();
            Require(result.Changed.Count == 2 && result.RestartRequired.SequenceEqual(["audio.device"]) && applied.Count == 2 && !store.HasPending, "Aplicar runs the appliers and reports what needs a restart");
            Require((int)store.Value("graphics.fps_limit") == 60 && applied.Any(a => a.Item1 == "graphics.fps_limit" && (int)a.Item2 == 60), "the applier got the value");
            store.Set("graphics.fps_limit", 60); Require(!store.HasPending, "setting the value already in use is no change");
            store.Set("voice.noise", true); Require(!store.HasPending && !(bool)store.Value("voice.noise"), "an unavailable setting cannot be changed");
            try { store.Set("nao.existe", 1); Require(false, "unknown id throws"); } catch (KeyNotFoundException) { }
            var again = new SettingsStore(Root, Catalog(), Presets());
            Require((int)again.Value("graphics.fps_limit") == 60 && (string)again.Value("audio.device") == "Fones", "applied values persist across restarts");

            // ---- reset
            store.ResetCategory(SettingCategory.Graphics); Require((int)store.Effective("graphics.fps_limit") == 120 && (string)store.Effective("audio.device") == "Fones", "restoring a category touches only that category");
            store.Discard();
            store.ResetAll(); Require(store.IsDefault("audio.device") && store.IsDefault("graphics.fps_limit"), "restoring everything proposes every default");
            store.Discard();

            // ---- presets
            store.Set("graphics.preset", "low");
            Require((int)store.Effective("graphics.msaa") == 0 && !(bool)store.Effective("graphics.shadows") && (string)store.Effective("graphics.preset") == "low", "choosing a preset proposes its values");
            store.Apply();
            store.Set("graphics.msaa", 4); Require((string)store.Effective("graphics.preset") == "custom", "changing a value the preset controls makes it Personalizada");
            store.Set("graphics.preset", "high"); Require((int)store.Effective("graphics.msaa") == 4 && (bool)store.Effective("graphics.shadows"), "choosing a preset again gives its values");
            store.Set("graphics.vsync", false); Require((string)store.Effective("graphics.preset") == "high", "a setting the preset does not control leaves it alone");
            store.Discard();

            // ---- profiles
            store.Set("audio.master", .3); store.Apply();
            store.Create("Jogo leve"); store.Create("Gravação", copyCurrent: true);
            Require(store.Profiles().SequenceEqual(["Padrão", "Gravação", "Jogo leve"]), $"profiles are listed, the default first ({string.Join(",", store.Profiles())})");
            try { store.Create("jogo LEVE"); Require(false, "duplicate names are refused"); } catch (InvalidOperationException) { }
            try { store.Create("   "); Require(false, "blank names are refused"); } catch (ArgumentException) { }
            try { store.Delete("Padrão"); Require(false, "the default cannot be deleted"); } catch (InvalidOperationException) { }
            try { store.Rename("Padrão", "Outro"); Require(false, "the default cannot be renamed"); } catch (InvalidOperationException) { }
            applied.Clear(); store.Switch("Jogo leve");
            Require(store.ActiveProfile == "Jogo leve" && Mathf.IsEqualApprox((float)store.Value("audio.master"), 1f) && applied.Any(a => a.Item1 == "audio.master"), "switching loads the profile and applies it");
            store.Set("graphics.fps_limit", 30); store.Apply();
            Require(new SettingsStore(Root, Catalog(), Presets()).ActiveProfile == "Jogo leve", "the active profile is remembered");
            store.Switch("Gravação"); Require(Mathf.IsEqualApprox((float)store.Value("audio.master"), .3f), "a copy keeps the values it was made with");
            var copy = store.Duplicate("Jogo leve", "Cópia"); store.Switch(copy); Require((int)store.Value("graphics.fps_limit") == 30, "a duplicate has the same values");
            store.Rename("Cópia", "Renomeado"); Require(store.ActiveProfile == "Renomeado" && store.Profiles().Contains("Renomeado") && !store.Profiles().Contains("Cópia"), "renaming the active profile keeps it active");
            store.Delete("Renomeado"); Require(store.ActiveProfile == "Padrão" && !store.Profiles().Contains("Renomeado"), "deleting the active profile goes back to the default");
            for (var i = store.Profiles().Count; i < SettingsStore.MaxProfiles; i++) store.Create($"p{i}");
            try { store.Create("a mais"); Require(false, "there is a limit"); } catch (InvalidOperationException) { }
            foreach (var name in store.Profiles().Where(n => n.StartsWith("p") && n != "Padrão")) store.Delete(name);

            // ---- export and import
            var path = ProjectSettings.GlobalizePath($"{Root}/exportado.json");
            store.Export("Jogo leve", path);
            var imported = store.Import(path);
            Require(imported == "Jogo leve (2)", $"an imported profile never takes a name in use ({imported})");
            store.Switch(imported); Require((int)store.Value("graphics.fps_limit") == 30, "the imported profile has the exported values");
            store.Switch("Padrão");
            File.WriteAllText(path, "{\"formato\":\"lifeverse-configuracoes\",\"versao\":2,\"nome\":\"Hostil\",\"valores\":{\"graphics.fps_limit\":99999,\"audio.master\":\"abc\",\"token\":\"segredo\",\"graphics.preset\":\"inventado\"}}");
            var hostile = store.Import(path); store.Switch(hostile);
            Require((int)store.Value("graphics.fps_limit") == 360 && (float)store.Value("audio.master") == 1f && (string)store.Value("graphics.preset") == "high", "imported values are checked and limited");
            Require(!File.ReadAllText(ProjectSettings.GlobalizePath($"{Root}/{SettingsStore.SafeFileName(hostile)}.cfg")).Contains("segredo"), "unknown keys are not kept");
            store.Switch("Padrão");
            foreach (var bad in new[] { "isso não é json", "{\"formato\":\"outro\",\"valores\":{}}", "[1,2]", "{\"formato\":\"lifeverse-configuracoes\"}" })
            { File.WriteAllText(path, bad); try { store.Import(path); Require(false, $"invalid file refused: {bad}"); } catch (InvalidDataException) { } }
            File.WriteAllText(path, new string(' ', SettingsStore.MaxImportBytes + 10)); try { store.Import(path); Require(false, "a huge file is refused"); } catch (InvalidDataException) { }

            // ---- schema migration
            var old = new ConfigFile(); old.SetValue("meta", "name", "Antigo"); old.SetValue("meta", "schema_version", 1); old.SetValue("values", "gfx.vsync", false); old.SetValue("values", "gfx.fps", 200); old.SetValue("values", "lixo", "x");
            old.Save($"{Root}/antigo.cfg");
            store.Switch("Antigo");
            Require(!(bool)store.Value("graphics.vsync") && (int)store.Value("graphics.fps_limit") == 200, "an older profile keeps its values under the new names");
            var rewritten = new ConfigFile(); rewritten.Load($"{Root}/antigo.cfg");
            Require(rewritten.GetValue("meta", "schema_version").AsInt32() == SettingsStore.SchemaVersion && !rewritten.HasSectionKey("values", "gfx.vsync") && !rewritten.HasSectionKey("values", "lixo"), "and is saved again in the new schema");
            store.Switch("Padrão");

            // ---- legacy files
            Wipe();
            var legacy = new ConfigFile(); legacy.SetValue("graphics", "fps", 144); legacy.SetValue("graphics", "vsync", false); legacy.SetValue("graphics", "quality", "ultra"); legacy.SetValue("audio", "master", .4); legacy.Save($"{Root}/settings_antigo.cfg");
            var viewer = new ConfigFile(); viewer.SetValue("audio", "volume", .75); viewer.Save($"{Root}/viewer_antigo.cfg");
            var fresh = new SettingsStore(Root, Catalog(), Presets());
            Require(fresh.ImportLegacy($"{Root}/settings_antigo.cfg", $"{Root}/viewer_antigo.cfg"), "the old files are imported the first time");
            Require((int)fresh.Value("graphics.fps_limit") == 140 && !(bool)fresh.Value("graphics.vsync") && Mathf.IsEqualApprox((float)fresh.Value("audio.master"), .75f), "their values arrive (the volume of the viewer menu wins)");
            Require((int)fresh.Value("graphics.msaa") == 4 && (bool)fresh.Value("graphics.shadows"), "a profile whose preset is not Personalizada has the values of that preset");
            fresh.Set("graphics.fps_limit", 60); fresh.Apply();
            Require(!fresh.ImportLegacy($"{Root}/settings_antigo.cfg", $"{Root}/viewer_antigo.cfg") && (int)fresh.Value("graphics.fps_limit") == 60, "they are imported once, never over what the person set later");
            Require(!new SettingsStore(Root, Catalog(), Presets()).ImportLegacy($"{Root}/nao_existe.cfg", $"{Root}/nao_existe2.cfg"), "and nothing happens when there are no old files");

            // ---- the real catalog
            Require(SettingsCatalog.All.Select(d => d.Id).Distinct().Count() == SettingsCatalog.All.Count, "setting ids are unique");
            foreach (var d in SettingsCatalog.All)
            {
                Require(d.Normalize(d.Default).Equals(d.Default), $"{d.Id}: the default is valid");
                Require(d.Label.Length > 0 && d.Description.Length > 10 && d.Tab.Length > 0, $"{d.Id}: has a name, a tab and a help text");
                if (d.Kind == SettingKind.Choice) Require(d.Choices.Any(c => c.Id == (string)d.Default) || (d.ChoiceSource is not null && (string)d.Default == "auto"), $"{d.Id}: the default is one of the choices (or \"auto\" for what the device offers)");
            }
            Require(SettingCategories.Labels.Length == 18 && Enum.GetValues<SettingCategory>().Length == 18 && SettingCategories.LabelOf(SettingCategory.Interface) == "Interface e HUD", "the 18 categories");
            Require(SettingsCatalog.All.Where(d => d.Id == "graphics.preset").All(d => d.Choices.Count == 7), "the quality preset has the seven options");

            Wipe();
            GD.Print("event=settings_store_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=settings_store_smoke result=failed error={exception.Message} {exception.StackTrace}"); GetTree().Quit(1); }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
