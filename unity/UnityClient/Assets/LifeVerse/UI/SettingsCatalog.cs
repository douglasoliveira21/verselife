using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace LifeVerse.Unity.UI
{
    /// <summary>One setting as the Godot client defines it: where it lives (category, tab, group), how it is shown, the values it takes and what it starts as.</summary>
    public sealed class SettingDef
    {
        public readonly string Id, Category, Tab, Kind, Default; public string Label, Help;
        public double Min, Max = 1, Step; public string Unit = "", Group = "", Unavailable, ButtonText = "Executar", Confirm; public int Column;
        public bool RestartRequired, PresetControlled, Device;
        public (string Id, string Label)[] Choices = Array.Empty<(string, string)>();
        public string[] Keywords = Array.Empty<string>();
        public SettingDef(string id, string category, string tab, string label, string help, string kind, string @default) { Id = id; Category = category; Tab = tab; Label = label; Help = help; Kind = kind; Default = @default; }
        public bool IsValue => Kind != "Action" && Kind != "Info";
        public bool IsAvailable => Unavailable == null;
    }

    /// <summary>
    /// The settings of the game: the Godot client's catalog (the same 196 ids, labels, texts, defaults and limits, see <c>SettingsCatalog.Generated.cs</c>) plus the few the Unity client had of its own. For each one,
    /// <see cref="InUnity"/> says whether it acts in this client; the ones that do not are shown disabled with the reason, never pretended. Values are normalized here (type, range, step, known choice).
    /// </summary>
    public static partial class SettingsCatalog
    {
        public static readonly (string Id, string Label)[] Categories =
        {
            ("General", "Geral e conta"), ("Graphics", "Gráficos e desempenho"), ("Audio", "Áudio"), ("Voice", "Comunicação por voz"), ("Controls", "Controles e movimentação"), ("Camera", "Câmera e visualização"),
            ("Avatar", "Avatar e aparência"), ("Chat", "Chat e social"), ("Privacy", "Privacidade e segurança"), ("Notifications", "Notificações"), ("Inventory", "Inventário e conteúdo"), ("Building", "Construção e edição"),
            ("World", "Mundo, ilhas e teleportes"), ("Scripts", "Scripts e interações"), ("Network", "Rede e conectividade"), ("Interface", "Interface e HUD"), ("Accessibility", "Acessibilidade"), ("Advanced", "Avançado e diagnóstico"),
        };

        public const string NotYet = "Ainda não tem efeito no cliente Unity.";

        /// <summary>The settings the Unity client had before the catalog came over, which the Godot one does not list (or lists as unavailable while Unity has them).</summary>
        private static readonly SettingDef[] Extra =
        {
            new SettingDef("audio.music", "Audio", "Volumes", "Música", "Volume das músicas das ilhas e dos terrenos.", "Float", "0.8") { Min = 0, Max = 1, Step = .05, Group = "Volumes", Keywords = new[] { "música", "volume" } },
            new SettingDef("audio.effects", "Audio", "Volumes", "Efeitos", "Volume dos sons do mundo e dos objetos.", "Float", "1") { Min = 0, Max = 1, Step = .05, Group = "Volumes", Keywords = new[] { "efeitos", "sons", "volume" } },
            new SettingDef("audio.ambient", "Audio", "Volumes", "Ambiente", "Volume do som de fundo (mar, vento, chuva).", "Float", "0.8") { Min = 0, Max = 1, Step = .05, Group = "Volumes", Keywords = new[] { "ambiente", "mar", "vento", "volume" } },
            new SettingDef("audio.media", "Audio", "Volumes", "Mídia", "Volume dos vídeos e sons tocados por objetos.", "Float", "1") { Min = 0, Max = 1, Step = .05, Group = "Volumes", Keywords = new[] { "mídia", "vídeo", "volume" } },
            new SettingDef("audio.parcel_music", "Audio", "Volumes", "Tocar a música dos terrenos", "Toca a música que o dono do terreno (ou da ilha) escolheu enquanto você está nele.", "Bool", "true") { Group = "Música", Keywords = new[] { "música", "terreno" } },
            new SettingDef("audio.ui_sounds", "Audio", "Volumes", "Sons da interface", "Pequenos sons ao clicar, avisar e errar.", "Bool", "true") { Group = "Música", Keywords = new[] { "cliques", "interface", "sons" } },
            new SettingDef("accessibility.large_text", "Accessibility", "Leitura", "Texto maior", "Aumenta as letras da interface.", "Bool", "false") { Group = "Leitura", Keywords = new[] { "texto", "letras", "fonte" } },
            new SettingDef("camera.focus_double_click", "Camera", "Câmera livre", "Duplo clique foca em pessoas e objetos", "Um duplo clique em uma pessoa ou objeto põe a câmera nele. No chão, o duplo clique continua levando o avatar até o ponto.", "Bool", "false") { Group = "Câmera livre", Keywords = new[] { "duplo clique", "foco", "câmera" } },
            new SettingDef("camera.allow_detached", "Camera", "Câmera livre", "Permitir a câmera desacoplada", "ALT + V solta a câmera do avatar: as teclas de andar a movem pelo mundo e o avatar fica parado. Ela não passa do alcance abaixo. Esc ou ALT + V de novo a devolvem.", "Bool", "false") { Group = "Câmera livre", Keywords = new[] { "desacoplada", "solta", "câmera" } },
            new SettingDef("camera.detached_range", "Camera", "Câmera livre", "Alcance da câmera desacoplada", "A que distância do avatar a câmera desacoplada pode ir (metros).", "Int", "60") { Min = 5, Max = 200, Step = 5, Group = "Câmera livre", Keywords = new[] { "desacoplada", "alcance" } },
            new SettingDef("camera.detached_speed", "Camera", "Câmera livre", "Velocidade da câmera desacoplada", "Quantos metros por segundo ela anda (Shift anda três vezes mais).", "Float", "8") { Min = 1, Max = 60, Step = 1, Group = "Câmera livre", Keywords = new[] { "desacoplada", "velocidade" } },
            new SettingDef("interface.appearance", "Interface", "Geral", "Aparência da interface", "Papel claro ou escuro. Automática acompanha o céu que você vê: clara de dia, escura à noite.", "Choice", "auto") { Group = "Tamanho e modo", Column = 0, Choices = new[] { ("auto", "Automática (segue o céu)"), ("day", "Sempre clara"), ("night", "Sempre escura") }, Keywords = new[] { "tema", "noite", "dia", "escuro", "claro", "cores" } },
            new SettingDef("account.sync", "General", "Conta", "Guardar as configurações na conta", "Leva as configurações e as teclas para outros computadores em que você entrar.", "Bool", "false") { Group = "Conta", Keywords = new[] { "sincronizar", "nuvem", "conta" } },
        };

        /// <summary>Godot marks these as unavailable there; the Unity client has them.</summary>
        private static readonly HashSet<string> UnityHas = new HashSet<string> { "voice.echo_cancel", "accessibility.contrast", "audio.interface", "network.ping", "perf.object_lod", "perf.resource_cache", "network.download_limit", "metrics.cpu_gpu", "advanced.verify_files", "accessibility.captions" };

        /// <summary>The settings that act in the Unity client (by a value bound to the old settings, or an effect applied in the world, the interface, the audio or the screen), plus every Info and Action handled here.</summary>
        public static readonly HashSet<string> InUnity = new HashSet<string>
        {
            // bound to the properties the Unity client already had
            "graphics.preset", "graphics.vsync", "graphics.fps_limit", "audio.master", "voice.users_volume", "voice.input_device", "voice.sensitivity", "voice.echo_cancel", "controls.mouse_sensitivity", "controls.invert_y", "controls.always_run",
            "chat.opacity", "chat.font_size", "interface.scale", "accessibility.reduce_motion", "accessibility.contrast", "notifications.toast_grupos", "notifications.toast_convites", "notifications.toast_sistema", "privacy.search_history",
            "audio.music", "audio.effects", "audio.ambient", "audio.media", "audio.parcel_music", "audio.ui_sounds", "accessibility.large_text", "account.sync",
            // applied by the Unity client
            "display.monitor", "display.resolution", "display.mode", "perf.render_distance", "graphics.shadow_distance", "graphics.shadows_enabled", "graphics.anisotropic", "graphics.texture_quality", "graphics.particles",
            "metrics.fps", "metrics.frame_time", "metrics.memory", "audio.mute_background", "perf.background_fps",
            "camera.default_mode", "camera.fov", "camera.wheel_first_person", "camera.fp_fov", "camera.fp_bob", "camera.fp_bob_strength", "camera.fp_sway", "camera.fp_sway_strength", "camera.fp_stabilization", "camera.default_distance",
            "camera.zoom_step", "camera.height", "camera.shoulder", "camera.smoothing", "camera.collision",
            "world.max_lights", "world.max_light_shadows", "notifications.toast_seconds", "notifications.max_toasts", "notifications.dnd", "inventory.default_sort", "inventory.subfolders", "build.snap", "build.move_step", "avatar.footsteps",
            // shown and done here
            "general.updates", "account.user", "account.server", "account.connection", "account.session", "account.leave_island", "account.quit", "advanced.version", "advanced.godot", "advanced.system", "advanced.gpu", "advanced.renderer",
            "advanced.profile", "advanced.data_folder", "advanced.open_data", "advanced.open_logs", "advanced.export_diagnostics", "advanced.restore_all", "interface.restore_layout", "interface.restore_all", "interface.restore_toolbar", "interface.mode", "graphics.fog", "graphics.volumetric", "graphics.glow", "graphics.color_adjust", "graphics.ssao", "graphics.ssao_quality", "graphics.ssr", "graphics.shadow_cascades", "graphics.shadow_quality", "graphics.msaa", "graphics.render_scale", "graphics.dynamic_resolution", "chat.show_time", "chat.visible_seconds", "avatar.show_names", "avatar.show_own_name", "world.show_coords", "world.minimap_zoom", "world.minimap_avatars", "interface.window_opacity", "interface.map_opacity", "interface.radial_size", "interface.radial_opacity", "interface.radial_glow", "scripts.font_size", "scripts.indent", "scripts.autocomplete", "build.rotate_step", "build.scale_step", "advanced.clear_cache", "network.auto_reconnect", "interface.compact_toolbar", "camera.photo_format", "camera.photo_include_ui", "avatar.open_editor", "chat.open_people",
            "network.state", "network.protocol", "network.avatars",
            // the rest of the Godot settings, done in this client too
            "avatars.max_full", "avatars.max_reduced", "avatars.visibility_distance", "avatars.triangle_budget", "avatars.lod", "avatars.simplify_distant", "avatars.animation_quality", "world.object_distance", "perf.auto_quality", "graphics.water_waves",
            "voice.enabled", "voice.open_mic", "voice.hearing_distance", "voice.spatial", "voice.indicators", "controls.double_tap_run", "controls.hold_to_fly", "camera.fp_look_smoothing", "camera.fp_collision",
            "world.minimap_open", "world.minimap_rotate", "world.minimap_names", "world.minimap_buildings", "world.map_parcels", "world.map_friends", "world.map_names", "world.map_events", "world.map_favorites", "world.map_featured",
            "interface.radial_speed", "interface.radial_tooltips", "general.keep_on_top", "general.download_update", "avatar.show_titles", "avatar.default_animations", "avatar.open_mirror", "privacy.external_media", "build.space", "build.import_collision",
            "audio.interface", "network.ping", "perf.object_lod", "perf.resource_cache", "network.download_limit", "metrics.cpu_gpu", "advanced.verify_files", "accessibility.captions",
            "interface.appearance",
            "accessibility.keyboard", "camera.focus_double_click", "camera.allow_detached", "camera.detached_range", "camera.detached_speed",
            "privacy.online_visibility", "privacy.private_messages", "privacy.group_invites", "privacy.friend_requests", "privacy.blocked", "account.sessions",
        };

        /// <summary>Kept and enforced by the platform, not by this computer: here each one is a button that opens its page of "Privacidade e sessões".</summary>
        public static readonly Dictionary<string, string> AccountPages = new Dictionary<string, string>
        {
            ["privacy.online_visibility"] = "quem", ["privacy.private_messages"] = "quem", ["privacy.group_invites"] = "quem", ["privacy.friend_requests"] = "quem", ["privacy.blocked"] = "bloqueados", ["account.sessions"] = "sessoes",
        };

        /// <summary>Settings this client cannot honour, with the reason shown instead of the control.</summary>
        public static readonly Dictionary<string, string> UnityCannot = new Dictionary<string, string>
        {
            ["audio.output_device"] = "Neste cliente o som sai sempre pelo dispositivo padrão do Windows: o motor não deixa escolher outro. Troque a saída nas configurações de som do Windows.",
            ["interface.side_bar"] = "Este cliente não tem a barra lateral: os mesmos atalhos ficam na barra de ferramentas e no botão Mais (Eu, Comunicar, Mundo, Conteúdo).",
        };

        private static List<SettingDef> _all;

        /// <summary>Every setting, in the order of the window; the ones that do nothing in Unity carry <see cref="SettingDef.Unavailable"/>.</summary>
        public static IReadOnlyList<SettingDef> All
        {
            get
            {
                if (_all != null) return _all;
                var list = new List<SettingDef>();
                foreach (var raw in Raw)
                {
                    var d = !AccountPages.ContainsKey(raw.Id) ? raw : new SettingDef(raw.Id, raw.Category, raw.Tab, raw.Label, raw.Help, "Action", "") { ButtonText = raw.Id == "privacy.blocked" || raw.Id == "account.sessions" ? "Ver" : "Escolher", Group = raw.Group, Column = raw.Column, Keywords = raw.Keywords };
                    if (UnityHas.Contains(d.Id)) d.Unavailable = null;
                    else if (UnityCannot.TryGetValue(d.Id, out var why)) d.Unavailable = why;
                    else if (d.Unavailable == null && !InUnity.Contains(d.Id)) d.Unavailable = NotYet;
                    if (d.Id == "network.download_limit") { d.Label = "Limite de download (MB por segundo)"; d.Help = "A velocidade máxima com que o jogo baixa modelos, texturas e sons. 0: sem limite."; d.Min = 0; d.Max = 100; d.Step = 1; d.Unit = " MB/s"; }
                    if (d.Id == "metrics.cpu_gpu") d.Help = "Mostra no painel de desempenho quanto de cada quadro o processador e a placa de vídeo passam trabalhando.";
                    if (d.Id == "accessibility.captions") d.Help = "Escreve na tela quem está falando por voz e que sons tocam por perto (a fala não é transcrita).";
                    if (d.Id == "advanced.verify_files") d.Help = "Ligada, confere ao abrir o jogo os arquivos instalados e os baixados; os downloads estragados são apagados.";
                    if (d.Id == "advanced.godot") { d.Label = "Versão do Unity"; d.Help = "A versão do motor gráfico deste cliente."; }
                    if (d.Id == "graphics.msaa") d.Help = "Suaviza o serrilhado das bordas. Neste cliente: 2x usa FXAA, 4x usa SMAA e 8x a suavização temporal (TAA), feitas depois de desenhar.";
                    list.Add(d);
                }
                foreach (var e in Extra) { var at = list.FindLastIndex(d => d.Category == e.Category); list.Insert(at + 1, e); }
                return _all = list;
            }
        }

        public static SettingDef Find(string id) => All.FirstOrDefault(d => d.Id == id);
        public static string CategoryLabel(string id) => Categories.FirstOrDefault(c => c.Id == id).Label ?? id;
        public static IEnumerable<SettingDef> Of(string category) => All.Where(d => d.Category == category);

        /// <summary>The settings that match a search: every word must be found in the label, the help, the keywords, the tab or the category.</summary>
        public static List<SettingDef> Search(string text)
        {
            var words = Fold(text).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); if (words.Length == 0) return new List<SettingDef>();
            return All.Where(d => { var hay = Fold(d.Label + " " + d.Help + " " + string.Join(" ", d.Keywords) + " " + d.Tab + " " + CategoryLabel(d.Category)); return words.All(hay.Contains); }).ToList();
        }

        /// <summary>Lower case without accents, for the search.</summary>
        public static string Fold(string text)
        {
            var normal = (text ?? "").ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD); var sb = new System.Text.StringBuilder();
            foreach (var c in normal) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c); return sb.ToString();
        }

        /// <summary>The value a setting takes for what was typed or read: the type, the range, the step, a known choice, or the default when it makes no sense.</summary>
        public static string Normalize(SettingDef d, string raw)
        {
            raw = (raw ?? "").Trim();
            switch (d.Kind)
            {
                case "Bool": return bool.TryParse(raw, out var b) ? (b ? "true" : "false") : raw == "1" ? "true" : raw == "0" ? "false" : d.Default;
                case "Int":
                case "Float":
                {
                    if (!double.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || double.IsNaN(n) || double.IsInfinity(n)) return d.Default;
                    n = Math.Min(d.Max, Math.Max(d.Min, n)); if (d.Step > 0) n = Math.Min(d.Max, Math.Max(d.Min, d.Min + Math.Round((n - d.Min) / d.Step) * d.Step));
                    return d.Kind == "Int" ? ((long)Math.Round(n)).ToString(CultureInfo.InvariantCulture) : Math.Round(n, 4).ToString("0.####", CultureInfo.InvariantCulture);
                }
                case "Choice": return d.Device ? (raw.Length > 0 && raw.Length <= 64 ? raw : d.Default) : d.Choices.Any(c => c.Id == raw) ? raw : d.Default;
                case "Text": return raw.Length > 64 ? raw.Substring(0, 64) : raw;
                default: return d.Default;
            }
        }

        public static float F(string value, float fallback = 0f) => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : fallback;
        public static string S(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
