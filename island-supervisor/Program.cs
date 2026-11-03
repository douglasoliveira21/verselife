using System.Runtime.InteropServices;
using LifeVerse.IslandSupervisor;

// Runs inside the island image: starts a Godot island server for each island players ask to enter.
static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new InvalidOperationException($"{name} is required.");
static int Number(string name, int fallback) => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
static string Optional(string name, string fallback) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

var apiUrl = Required("LIFEVERSE_API_URL");
var serviceKey = Required("LIFEVERSE_SERVICE_KEY");
if (serviceKey.Length < 32) throw new InvalidOperationException("LIFEVERSE_SERVICE_KEY must contain at least 32 characters.");
var options = new SupervisorOptions(
    PortStart: Number("SUPERVISOR_PORT_START", 7777), MaxIslands: Math.Clamp(Number("SUPERVISOR_MAX_ISLANDS", 8), 1, 64), HealthPortStart: Number("SUPERVISOR_HEALTH_PORT_START", 7070),
    BaseEnvironment: new Dictionary<string, string>
    {
        ["LIFEVERSE_API_URL"] = apiUrl, ["LIFEVERSE_SERVICE_KEY"] = serviceKey, ["LIFEVERSE_HOSTNAME"] = Optional("LIFEVERSE_HOSTNAME", Environment.MachineName),
        ["LIFEVERSE_IDLE_SHUTDOWN_MINUTES"] = Optional("SUPERVISOR_IDLE_MINUTES", "10"), ["GODOT_HEADLESS"] = "1"
    });

void Log(string message) => Console.WriteLine($"{{\"timestamp\":\"{DateTimeOffset.UtcNow:O}\",\"event\":\"supervisor\",\"message\":{System.Text.Json.JsonSerializer.Serialize(message)}}}");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
http.DefaultRequestHeaders.Add("X-LifeVerse-Service-Key", serviceKey);
var launcher = new SystemProcessLauncher(Optional("SUPERVISOR_GODOT_PATH", "godot"), Optional("SUPERVISOR_PROJECT_PATH", "/app/island-server"), Console.WriteLine);
var supervisor = new IslandProcessSupervisor(options, new HttpPendingIslands(http, apiUrl), launcher, () => DateTimeOffset.UtcNow, Log);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; cancellation.Cancel(); });

Log($"supervisor started: UDP {options.PortStart}-{options.PortStart + options.MaxIslands - 1}, idle shutdown {options.BaseEnvironment!["LIFEVERSE_IDLE_SHUTDOWN_MINUTES"]} min");
var poll = TimeSpan.FromSeconds(Math.Clamp(Number("SUPERVISOR_POLL_SECONDS", 3), 1, 60));
try
{
    while (!cancellation.IsCancellationRequested)
    {
        await supervisor.TickAsync(cancellation.Token);
        await Task.Delay(poll, cancellation.Token);
    }
}
catch (OperationCanceledException) { }
Log("stopping island servers");
supervisor.StopAll(TimeSpan.FromSeconds(15));
