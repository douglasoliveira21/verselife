using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LifeVerse.Shared.Networking;
using LifeVerse.Unity.Avatar;
using LifeVerse.Unity.UI;
using LifeVerse.Unity.World;
using UnityEngine;

namespace LifeVerse.Unity.Server
{
    /// <summary>
    /// Plays the real game against a real LifeVerse (API + island supervisor + Unity island server), the way a person does, and says whether it worked:
    ///   LifeVerse.exe -screen-fullscreen 0 -lifeverse-e2e http://127.0.0.1:5080 usuario senha
    /// The login screen signs in with "Onde parei" (the server chooses the island of this engine), the island's server is started by the supervisor, the avatar is born, walks, says something in the
    /// chat and hears its own words back from the island. Each step prints "event=e2e_step name=… result=ok|FAIL detail=…"; the run ends with "event=e2e result=passed|failed". Nothing of production is
    /// touched unless the address given is production: use only a test API.
    /// </summary>
    public sealed class EndToEndCheck : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, "-lifeverse-e2e"); if (index < 0 || index + 3 >= args.Length) return;
            var check = new GameObject("End To End Check").AddComponent<EndToEndCheck>(); check.Url = args[index + 1]; check.User = args[index + 2]; check.Password = args[index + 3];
        }

        public string Url, User, Password;
        private readonly List<(string Name, bool Ok, string Detail)> _results = new List<(string, bool, string)>();

        private void Record(string name, bool ok, string detail = "") { _results.Add((name, ok, detail)); Console.WriteLine($"event=e2e_step name={name} result={(ok ? "ok" : "FAIL")} detail={detail}"); }

        private static IEnumerator Until(Func<bool> condition, float seconds) { var end = Time.realtimeSinceStartup + seconds; while (!condition() && Time.realtimeSinceStartup < end) yield return null; }

        private IEnumerator Start()
        {
            Application.targetFrameRate = 60; var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lifeverse-e2e-" + Guid.NewGuid().ToString("N")); System.IO.Directory.CreateDirectory(folder);
            var app = new GameObject("App").AddComponent<GameApp>(); app.SettingsPath = System.IO.Path.Combine(folder, "settings.json"); app.SavedLoginPath = System.IO.Path.Combine(folder, "login.sealed");
            app.Begin(Url, new UserSettings { AutoScale = false }); yield return null; yield return null;
            yield return Until(() => app.Login.ServersNow != LoginView.Servers.Checking, 10f); Record("servers", app.Login.ServersNow == LoginView.Servers.Online, app.Login.ServerState.text);
            app.Login.User.value = User; app.Login.Password.value = Password; app.Login.Island.value = ""; app.Login.Submit();
            // the first time, the island asks for a character: it is made in the creator that opens by itself, as a person would
            var created = false; var end = Time.realtimeSinceStartup + 150f;
            while (Time.realtimeSinceStartup < end && !(app.Current == GameApp.Phase.Playing || (app.Current == GameApp.Phase.Login && !app.Login.Busy) || (app.Loading != null && app.Loading.Failed)))
            {
                if (!created) foreach (var child in app.Ui.WindowLayer.Children()) if (child is CharacterWindow creator) { creator.Draft.DisplayName = "Teste E2E"; creator.Save(); created = true; Record("character", true, "criado na primeira entrada"); break; }
                yield return null;
            }
            var entered = app.Current == GameApp.Phase.Playing && app.Client != null && app.Client.Authenticated;
            Record("enter", entered, entered ? "island " + app.Island + " (" + app.StartReason + ")" : "phase " + app.Current + " · " + app.Login.Status.text + " · " + (app.Loading != null ? app.Loading.StatusText : ""));
            if (entered)
            {
                yield return Until(() => app.Player != null && app.Player.Spawned, 20f); Record("spawn", app.Player != null && app.Player.Spawned, app.Player != null ? app.Player.GamePosition.ToString() : "");
                if (app.Player != null && app.Player.Spawned)
                {
                    var from = app.Player.GamePosition; app.Player.Scripted = () => new PlayerInputSample { Forward = 1f }; yield return new WaitForSeconds(2f); app.Player.Scripted = () => default; yield return new WaitForSeconds(.5f);
                    var moved = Vector3.Distance(app.Player.GamePosition, from); Record("walk", moved > 1f, moved.ToString("0.0") + " m");
                    var words = "teste e2e " + UnityEngine.Random.Range(1000, 9999); var heard = false; app.Client.ChatReceived += message => { if (message.Message.Contains(words)) heard = true; };
                    Record("chat_sent", app.Client.SendChat(words)); yield return Until(() => heard, 10f); Record("chat_back", heard, words);
                }
                app.BackToLogin();
            }
            yield return new WaitForSeconds(.5f);
            var failed = _results.Count(r => !r.Ok); Console.WriteLine($"event=e2e result={(failed == 0 ? "passed" : "failed")} steps={_results.Count} failed={failed}");
            try { System.IO.Directory.Delete(folder, true); } catch (Exception) { }
            CheckRun.Quit(failed == 0 ? 0 : 1);
        }
    }
}
