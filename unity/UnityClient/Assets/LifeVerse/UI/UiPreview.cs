using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// Photographs the interface on a real GPU and checks how it behaves with a real panel:
    ///   LifeVerse.exe -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -lifeverse-ui-shots C:\out\ui
    /// (in the editor, without -batchmode: EditorChecks.Play -lifeverse-editor-run OUT.log -lifeverse-ui-shots C:\out\ui)
    /// writes ui-login.png, ui-register.png, ui-loading.png, ui-hud.png (HUD, chat, notices, settings and help windows) and prints "event=ui_check result=passed|failed".
    /// </summary>
    public sealed class UiPreview : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, "-lifeverse-ui-shots"); if (index < 0 || index + 1 >= args.Length) return;
            new GameObject("UI Preview").AddComponent<UiPreview>().Prefix = args[index + 1];
        }

        public string Prefix; private int _checks;

        private static void Expect(bool condition, string what) { if (!condition) throw new InvalidOperationException(what); }

        private IEnumerator Shot(string name)
        {
            for (var i = 0; i < 20; i++) yield return null;
            var path = Prefix + "-" + name + ".png"; Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path); var waited = 0; while (!File.Exists(path) && waited++ < 120) yield return null;
            for (var i = 0; i < 5; i++) yield return null; Console.WriteLine($"event=ui_shot name={name} saved={File.Exists(path)}");
        }

        private IEnumerator Start()
        {
            string failure = null; var steps = Run();
            while (true) { try { if (!steps.MoveNext()) break; } catch (Exception exception) { failure = exception.Message; break; } yield return steps.Current; }
            Console.WriteLine(failure == null ? $"event=ui_check result=passed checks={_checks}" : $"event=ui_check result=failed error={failure}"); LifeVerse.Unity.World.CheckRun.Quit(failure == null ? 0 : 1);
        }

        private IEnumerator Run()
        {
            Application.targetFrameRate = 30;
            var camera = new GameObject("Camera") { tag = "MainCamera" }.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.35f, .5f, .62f); camera.gameObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
            var path = Path.Combine(Application.temporaryCachePath, "ui-preview-settings.json"); if (File.Exists(path)) File.Delete(path);
            var settings = new UserSettings { AutoScale = Array.IndexOf(Environment.GetCommandLineArgs(), "-lifeverse-editor-size") >= 0, InterfaceScale = 1f };   /* at another size, the automatic scale is what is checked */ var ui = UiRoot.Create(settings); var notices = new NotificationCenter(ui); var hud = new Hud(ui, notices);
            yield return null;

            // login
            var login = new LoginView(settings, "Versão de teste  •  Unity"); ui.ScreenLayer.Add(login); yield return null; yield return null;
            Expect(login.resolvedStyle.width > 100f && login.User.resolvedStyle.width > 50f, "a tela de entrada tem tamanho (o painel desenhou): " + login.resolvedStyle.width); _checks++;
            login.Island.value = Guid.NewGuid().ToString(); login.User.value = "douglas"; login.Fail("Usuário ou senha incorretos."); yield return Shot("login");
            login.ToggleMode(); yield return Shot("register");
            Expect(login.Registering && login.Email.resolvedStyle.display != DisplayStyle.None && login.Enter.text.Contains("Criar"), "o modo de cadastro mostra o e-mail e muda o botão"); _checks++;
            login.ToggleMode(); login.RemoveFromHierarchy();

            // loading
            var loading = new LoadingView(); ui.ScreenLayer.Add(loading); loading.SetStatus("waiting_island 2/15"); yield return Shot("loading");
            Expect(loading.StatusText.Contains("Iniciando a ilha") && loading.Progress > .2f, "a espera pelo servidor aparece em português"); _checks++;
            loading.RemoveFromHierarchy();

            // HUD
            hud.Show(); hud.SetIsland("Praia do Sol"); hud.SetPosition(new Vector3(128f, 4f, -62f)); hud.SetBalance(12450); hud.SetConnection(true, 3); hud.SetPeople("Douglas", new[] { "Ana", "Beto" }); hud.SetBell(2);
            hud.AddMenu("Eu", new (string, string, Action)[] { ("Inventário", "bag", null) }); hud.AddMenu("Comunicar", new (string, string, Action)[] { ("Pessoas", "people", null), ("Gestos", "hand", null) }); var world = new (string, string, Action)[] { ("Mapa do mundo", "globe", null), ("Buscar no LifeVerse", "search", null), (Hud.MenuLine, "", null), ("Sobre a ilha", "info", null), ("Tirar foto   F12", "photo", null) }; var worldMenu = hud.AddMenu("Mundo", world); hud.AddMenu("Conteúdo", new (string, string, Action)[0]); hud.AddMenu("Ajuda", new (string, string, Action)[0]);
            hud.Register("Chat", "chat", () => null); hud.Register("Pessoas", "people", () => null); foreach (var (label, id) in new[] { ("Social", "social"), ("Inventário", "inventory"), ("Carteira", "wallet"), ("Buscar", "search"), ("Construir", "build"), ("Voz", "voice"), ("Avisos", "notifications"), ("Mapa", "map"), ("Mais", "more") }) hud.Register(label, id, () => null); hud.Register("Configurações", "settings", () => new SettingsWindow(ui, path)); hud.Register("Ajuda", "help", () => Hud.HelpWindow(ui));
            foreach (var (label, id) in new[] { ("Gestos", "gestures"), ("Foto", "photo"), ("Mini-mapa", "minimap") }) hud.Register(label, id, () => null);
            hud.Register("Mover", "move", () => new MoveWindow(ui, () => null, () => null)); hud.Register("Câmera", "camera", () => new CameraWindow(ui, () => null, () => 0f));
            hud.SetDefaultOrder("chat", "voice", "gestures", "people", "map", "build", "inventory", "photo", "move", "camera", "minimap", "search", "wallet", "notifications"); hud.ApplyLayout("full", false, null, Hud.DefaultHidden); UiKit.Alert(hud.BottomBar.Q<Button>("btn-notifications"), true);
            var island = new LifeVerse.Shared.Terrain.TerrainMap(); island.Apply(LifeVerse.Shared.Terrain.TerrainTool.Raise, 20, 10, 60, 14f); hud.Map.Paint(island); hud.Map.Place(new Vector3(12f, 2f, -20f), new[] { new Vector3(30f, 2f, -10f), new Vector3(-24f, 2f, 18f) }, new[] { "Ana", "Beto" });
            hud.Chat.Add(7, "Ana", "Oi, tudo bem?", DateTimeOffset.Now.AddMinutes(-2)); hud.Chat.Add(9, "Beto", "Cheguei agora, que ilha linda!", DateTimeOffset.Now.AddMinutes(-1)); hud.Chat.AddSystem("Você entrou na ilha."); hud.Chat.Add(7, "Ana", "Vem ver a casa que eu construí perto do lago <3", DateTimeOffset.Now);
            notices.Show("Beto chegou.", NoticeKind.Info); notices.Show("Conexão estável.", NoticeKind.Good); notices.Show("Sua mensagem não foi enviada.", NoticeKind.Warn);
            var sent = ""; hud.Chat.Send = text => { sent = text; return true; }; hud.Chat.Input.value = "  Olá \u0007 mundo  "; Expect(hud.Chat.Submit() && sent == "Olá  mundo", "o chat limpa e envia o texto: [" + sent + "]"); _checks++;
            hud.Toggle("settings", () => new SettingsWindow(ui, path)); hud.Toggle("help", () => Hud.HelpWindow(ui)); yield return null; yield return null;
            var window = hud.WindowOf("settings"); Expect(window != null && window.IsOpen && window.resolvedStyle.width > 100f, "a janela de configurações abre e desenha"); _checks++;
            Expect(notices.VisibleCount >= 1 && notices.Unread >= 3, "as notificações aparecem e contam como não lidas: " + notices.VisibleCount + " " + notices.Unread); _checks++;
            yield return Shot("hud");
            // the signature windows of the viewer, a menu, a list of choices and a hint
            window.Close(); hud.WindowOf("help")?.Close();
            hud.Toggle("move", () => new MoveWindow(ui, () => null, () => null)); hud.Toggle("camera", () => new CameraWindow(ui, () => null, () => 0f)); yield return null; yield return null;
            var move = (MoveWindow)hud.WindowOf("move"); move.SetRunning(true); move.Forward.Set(true); Expect(move.Sample().Forward == 1f && move.Sample().Run, "segurar a seta anda, correndo quando pedido"); move.Forward.Set(false); Expect(move.Sample().Forward == 0f, "soltar para"); _checks++;
            yield return Shot("controls"); hud.OpenSheet(); yield return Shot("sheet"); Expect(hud.OpenMenu != null && hud.OpenMenu.resolvedStyle.width > 200f, "o menu desenha"); _checks++; hud.CloseMenu();
            ui.SetNight(true); hud.Chat.Add(7, "Ana", "A noite chegou.", DateTimeOffset.Now); notices.Show("Boa noite.", NoticeKind.Good); yield return Shot("night"); hud.OpenSheet(); yield return Shot("night-sheet"); hud.CloseMenu(); ui.SetNight(false);
            var minimized = hud.WindowOf("camera"); minimized.SetMinimized(true); yield return null; yield return null; Expect(minimized.Minimized && minimized.resolvedStyle.height < 60f, "minimizada fica só a barra do título: " + minimized.resolvedStyle.height); _checks++;
            ui.Hints.Show(hud.NavigationBar.Q<VisualElement>("address")); Expect(ui.Hints.Shown, "a dica aparece"); 
            var list = hud.Presence.Q(null, "unity-popup-field__input"); using (var press = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = list.worldBound.center })) { press.target = list; hud.Presence.SendEvent(press); }
            yield return Shot("dropdown"); ui.Hints.Hide(); minimized.Close(); move.Close(); ui.Root.panel.visualTree.Q<VisualElement>(null, "unity-base-dropdown")?.RemoveFromHierarchy();
            hud.Toggle("settings", () => new SettingsWindow(ui, path)); hud.Toggle("help", () => Hud.HelpWindow(ui)); yield return null; window = hud.WindowOf("settings");
            // the icons of the Godot client, all of them, drawn by the interface
            var bad = UiIconData.Icons.Keys.Where(k => IconShapes.Of(k).Count == 0).ToList(); Expect(bad.Count == 0, "todos os ícones viram desenho: " + string.Join(", ", bad)); _checks++;
            var sheet = new FloatingWindow(ui, "icons", "Ícones (" + UiIconData.Icons.Count + ")", 1240f, 680f, new Vector2(20, 20)); var grid = new VisualElement(); grid.style.flexDirection = FlexDirection.Row; grid.style.flexWrap = Wrap.Wrap; sheet.Body.Add(grid);
            foreach (var key in UiIconData.Icons.Keys) { var cell = new VisualElement(); cell.style.width = 108; cell.style.height = 52; cell.style.alignItems = Align.Center; cell.Add(new Icon(key, 26)); var name = UiKit.Text(key, "lv-small", "lv-muted"); name.style.fontSize = 10; cell.Add(name); grid.Add(cell); }
            sheet.Open(); yield return Shot("icons"); sheet.Close();
            UiKit.IconOnly(hud.BottomBar.Q<Button>("btn-chat"), true); Expect(UiKit.IconOf(hud.BottomBar.Q<Button>("btn-chat")) == "chat" && hud.BottomBar.Q<Button>("btn-chat").text == "Chat", "o botão tem ícone e continua dizendo o nome"); UiKit.IconOnly(hud.BottomBar.Q<Button>("btn-chat"), false); _checks++;
            // the settings window changes things for real
            ((SettingsWindow)window).Show("Interface"); yield return Shot("settings-interface");
            window = hud.WindowOf("settings"); window.Close(); Expect(hud.WindowOf("settings") == null, "fechar a janela a tira"); _checks++;
            hud.Toggle("help", () => Hud.HelpWindow(ui)); Expect(hud.WindowOf("help") == null, "o botão de novo fecha a janela aberta"); _checks++;
            // the build tool and its terrain, as they open before any island answers
            var context = new ApiContext { Ui = ui, Api = new LifeVerse.Unity.World.IslandEntryClient("http://127.0.0.1:1"), Notices = notices, CurrentIsland = () => Guid.Empty };
            var builder = new BuildEditor(context, () => null, () => null, () => Vector3.zero, () => null); var terrainTools = new TerrainEditor(context, () => null, () => null, () => null);
            var build = new BuildWindow(context, builder, new BuildLinks { Terrain = terrainTools }); build.Open(); build.SetMode(BuildMode.Create); yield return null; yield return null; yield return Shot("build-create");
            build.SetMode(BuildMode.Edit); build.Show("Objeto"); yield return null; yield return Shot("build-object"); Expect(build.resolvedStyle.width > 300f, "a janela de construção abre e desenha");
            build.SetMode(BuildMode.Land); build.Terrain.Select(LifeVerse.Shared.Terrain.TerrainTool.Paint); yield return null; yield return null; yield return Shot("terrain-paint"); Expect(build.Terrain.PaintVisible && build.Terrain.LayerButtons == 8, "o terreno mostra os oito materiais ao pintar"); _checks++; build.Close();           
        }
    }
}
