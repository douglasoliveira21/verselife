using System;
using System.Linq;
using System.Threading.Tasks;
using LifeVerse.IslandServer.Core;
using UnityEngine;
using IslandServerCore = LifeVerse.IslandServer.Core.IslandServer;

namespace LifeVerse.Unity.Server
{
    /// <summary>
    /// O servidor de ilha dentro do Unity Dedicated Server. Toda a regra mora em LifeVerse.IslandServer.Core (sem engine); este componente só dá o
    /// relógio (um passo por quadro, a taxa fixa em LIFEVERSE_TICK_RATE), liga o encerramento limpo (SIGTERM do Supervisor chega como Application.quit)
    /// e devolve o código de saída. Sobe sozinho no build de servidor (UNITY_SERVER) ou com o argumento -lifeverse-server.
    /// A configuração vem das mesmas variáveis de ambiente do servidor Godot.
    /// </summary>
    public sealed class IslandServerBehaviour : MonoBehaviour
    {
        private IslandServerCore _server;
        private Task _starting;
        private Task _stopping;
        private int _exitCode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
#if !UNITY_SERVER
            if (!Environment.GetCommandLineArgs().Contains("-lifeverse-server")) return;
#endif
            var host = new GameObject("LifeVerse Island Server");
            DontDestroyOnLoad(host);
            host.AddComponent<IslandServerBehaviour>();
        }

        private void Start()
        {
            try
            {
                var config = IslandServerConfig.FromEnvironment();
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = config.TickRate;
                _server = new IslandServerCore(config, new HttpIslandApi(config)) { Physics = new UnityIslandPhysics() };
                _starting = _server.StartAsync();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("{\"event\":\"server_start_failed\",\"properties\":{\"error\":" + System.Text.Json.JsonSerializer.Serialize(exception.Message) + "}}");
                _exitCode = 1;
                Application.Quit(1);
            }
        }

        private void Update()
        {
            if (_server == null) return;
            if (_starting is { IsFaulted: true }) { _exitCode = 1; _starting = null; Application.Quit(1); return; }
            if (_stopping != null) return;
            _server.Update(Time.unscaledDeltaTime);
            if (_server.StopRequested)
            {
                // pedido de reinício do admin, ilha vazia há muito tempo ou SIGTERM: salva a ilha e as pessoas, sai com código 0
                _stopping = _server.StopAsync().ContinueWith(_ => Application.Quit(_exitCode), TaskScheduler.Default);
            }
        }

        private void OnApplicationQuit()
        {
            if (_server == null) return;
            _server.RequestStop();
            // o Unity já está saindo: dá até 15 s para salvar e se retirar da API
            (_stopping ?? _server.StopAsync()).Wait(TimeSpan.FromSeconds(15));
        }
    }
}
