using LifeVerse.IslandServer.Core;

// Servidor de ilha LifeVerse (protocolo Wire 12) como processo .NET. Código de saída: 0 parada limpa (SIGTERM, Ctrl+C, reinício do admin, ilha vazia), 1 falha ao subir.
IslandServerConfig config;
try { config = IslandServerConfig.FromEnvironment(); }
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine($"{{\"event\":\"server_config_invalid\",\"properties\":{{\"error\":{System.Text.Json.JsonSerializer.Serialize(exception.Message)}}}}}");
    return 1;
}

var shutdown = new CancellationTokenSource(); // vive até o fim do processo: os manipuladores de saída ainda o chamam
Console.CancelKeyPress += (_, args) => { args.Cancel = true; shutdown.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => shutdown.Cancel();
System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM, context => { context.Cancel = true; shutdown.Cancel(); });

using var server = new IslandServer(config, new HttpIslandApi(config));
try { await server.StartAsync(shutdown.Token); }
catch (Exception) { return 1; } // o motivo já saiu no log (server_start_failed)
await server.RunAsync(shutdown.Token);
return 0;
