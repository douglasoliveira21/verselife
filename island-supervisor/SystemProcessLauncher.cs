using System.ComponentModel;
using System.Diagnostics;

namespace LifeVerse.IslandSupervisor;

/// <summary>Starts a headless Godot island server per island and forwards its output, tagged with the island.</summary>
public sealed class SystemProcessLauncher(string godotPath, string projectPath, Action<string> write) : IProcessLauncher
{
    public IChildProcess Start(IslandLaunch launch)
    {
        var info = new ProcessStartInfo(godotPath) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { "--headless", "--path", projectPath }) info.ArgumentList.Add(argument);
        foreach (var (key, value) in launch.Environment) info.Environment[key] = value;
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var tag = launch.IslandId.ToString("N")[..8];
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) write($"[{tag}] {e.Data}"); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) write($"[{tag}] {e.Data}"); };
        try { process.Start(); }
        catch (Win32Exception exception) { throw new InvalidOperationException($"cannot start '{godotPath}': {exception.Message}", exception); }
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
