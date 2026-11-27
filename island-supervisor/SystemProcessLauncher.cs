using System.ComponentModel;
using System.Diagnostics;

namespace LifeVerse.IslandSupervisor;

/// <summary>
/// Starts an island server process per island and forwards its output, tagged with the island. The Unity dedicated server is started as
/// "LifeVerseServer.x86_64 -batchmode -nographics -logFile -": it reads the LIFEVERSE_* variables and answers SIGTERM with a clean stop.
/// </summary>
public sealed class SystemProcessLauncher(string serverPath, IReadOnlyList<string> arguments, Action<string> write) : IProcessLauncher
{
    /// <summary>The Unity dedicated server (Linux build). "-logFile -" sends the Unity log to the standard output, where the supervisor tags it.</summary>
    public static SystemProcessLauncher ForUnity(string serverPath, Action<string> write) => new(serverPath, new[] { "-batchmode", "-nographics", "-logFile", "-" }, write);

    public IChildProcess Start(IslandLaunch launch)
    {
        var info = new ProcessStartInfo(serverPath) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        foreach (var (key, value) in launch.Environment) info.Environment[key] = value;
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var tag = launch.IslandId.ToString("N")[..8];
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) write($"[{tag}] {e.Data}"); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) write($"[{tag}] {e.Data}"); };
        try { process.Start(); }
        catch (Win32Exception exception) { throw new InvalidOperationException($"cannot start '{serverPath}': {exception.Message}", exception); }
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        return new Child(process);
    }

    private sealed class Child(Process process) : IChildProcess
    {
        public bool HasExited => process.HasExited;
        public int? ExitCode => process.HasExited ? process.ExitCode : null;

        public void Stop(TimeSpan grace)
        {
            if (process.HasExited) return;
            try { if (!OperatingSystem.IsWindows()) Process.Start("kill", $"-TERM {process.Id}")?.WaitForExit(2000); }
            catch (Win32Exception) { }
            if (!process.WaitForExit(grace)) process.Kill();
        }
    }
}
