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
            Console.WriteLine(failure == null ? $"event=ui_check result=passed checks={_checks}" : $"event=ui_check result=failed error={failure}"); Application.Quit(failure == null ? 0 : 1);
        }

        private IEnumerator Run()
        {
            Application.targetFrameRate = 30;
            var camera = new GameObject("Camera") { tag = "MainCamera" }.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.35f, .5f, .62f); camera.gameObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
            var path = Path.Combine(Application.temporaryCachePath, "ui-preview-settings.json"); if (File.Exists(path)) File.Delete(path);
            var settings = new UserSettings { AutoScale = false, InterfaceScale = 1f }; var ui = UiRoot.Create(settings); var notices = new NotificationCenter(ui); var hud = new Hud(ui, notices);
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
            hud.Show(); hud.SetIsland("Ilha 3f2b1c9a"); hud.SetConnection(true, 3);
            hud.Register("Chat", "chat", () => null); hud.Register("Pessoas", "people", () => null); hud.Register("Configurações", "settings", () => new SettingsWindow(ui, path)); hud.Register("Ajuda", "help", () => Hud.HelpWindow(ui));
            hud.Chat.Add(7, "Ana", "Oi, tudo bem?", DateTimeOffset.Now.AddMinutes(-2)); hud.Chat.Add(9, "Beto", "Cheguei agora, que ilha linda!", DateTimeOffset.Now.AddMinutes(-1)); hud.Chat.AddSystem("Você entrou na ilha."); hud.Chat.Add(7, "Ana", "Vem ver a casa que eu construí perto do lago <3", DateTimeOffset.Now);
            notices.Show("Beto chegou.", NoticeKind.Info); notices.Show("Conexão estável.", NoticeKind.Good); notices.Show("Sua mensagem não foi enviada.", NoticeKind.Warn);
            var sent = ""; hud.Chat.Send = text => { sent = text; return true; }; hud.Chat.Input.value = "  Olá \u0007 mundo  "; Expect(hud.Chat.Submit() && sent == "Olá  mundo", "o chat limpa e envia o texto: [" + sent + "]"); _checks++;
            hud.Toggle("settings", () => new SettingsWindow(ui, path)); hud.Toggle("help", () => Hud.HelpWindow(ui)); yield return null; yield return null;
            var window = hud.WindowOf("settings"); Expect(window != null && window.IsOpen && window.resolvedStyle.width > 100f, "a janela de configurações abre e desenha"); _checks++;
            yield return Shot("hud");
            // the settings window changes things for real
            ((SettingsWindow)window).Show("Interface"); yield return Shot("settings-interface");
            window = hud.WindowOf("settings"); window.Close(); Expect(hud.WindowOf("settings") == null, "fechar a janela a tira"); _checks++;
            hud.Toggle("help", () => Hud.HelpWindow(ui)); Expect(hud.WindowOf("help") == null, "o botão de novo fecha a janela aberta"); _checks++;
            // the build panel and the terrain tools, as they open before any island answers
            var context = new ApiContext { Ui = ui, Api = new LifeVerse.Unity.World.IslandEntryClient("http://127.0.0.1:1"), Notices = notices, CurrentIsland = () => Guid.Empty };
            var builder = new BuildEditor(context, () => null, () => null, () => Vector3.zero, () => null); var build = new BuildWindow(context, builder); build.Open(); build.Show("Criar"); yield return null; yield return null; yield return Shot("build-create");
            build.Show("Forma"); yield return null; yield return Shot("build-shape"); build.Show("Dados"); yield return null; yield return Shot("build-data"); Expect(build.resolvedStyle.width > 300f, "o painel de construção abre e desenha"); build.Close();
            var terrainTools = new TerrainEditor(context, () => null, () => null, () => null); var terrain = new TerrainWindow(context, terrainTools); terrain.Open(); terrain.Select(LifeVerse.Shared.Terrain.TerrainTool.Paint); yield return null; yield return null; yield return Shot("terrain-paint"); Expect(terrain.PaintVisible && terrain.LayerButtons == 8, "as ferramentas de terreno mostram os oito materiais ao pintar"); _checks++; terrain.Close();            Expect(notices.VisibleCount >= 1 && notices.Unread >= 3, "as notificações aparecem e contam como não lidas: " + notices.VisibleCount + " " + notices.Unread); _checks++;
        }
    }
}
