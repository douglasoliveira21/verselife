using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Loading;

namespace LifeVerse.Client.Tests;

/// <summary>The loading screen and the entry coordinator: every step follows something that really happened, the bar never reaches 100% early, failures offer only possible actions.</summary>
public partial class LoadingSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var session = GetNode<Autoloads.SessionManager>("/root/SessionManager"); session.Set("smoke-token", "smoke-refresh", "smoke");
            var entry = new WorldEntryCoordinator { Name = "WorldEntryCoordinator" }; AddChild(entry); await Frames(2);
            var screen = entry.Screen; var manager = entry.Manager;
            var left = 0; entry.LeaveToMenu = () => left++;

            // the first entry, step by step
            entry.BeginLocating(); await Frames(2);
            Require(screen.IsOpen && screen.TitleForTesting == "Entrando no LifeVerse..." && screen.RowCountForTesting == 9, "the loading opens at once with its steps");
            Require(manager.Step("session")!.State == StepState.Completed && manager.Step("locate")!.State == StepState.Running && manager.Progress < .2, "the session was checked and the island is being located");
            Require(screen.TipForTesting.StartsWith("Dica: "), "a tip is shown");
            entry.SetIslandForTesting(Guid.NewGuid(), "Tropical Island");
            entry.StateForTesting("requesting_ticket", "x"); entry.StateForTesting("requesting_ticket", "x"); // the API is asked again while the server starts: nothing changes twice
            Require(manager.Step("locate")!.State == StepState.Completed && manager.Step("server")!.State == StepState.Running && screen.BarIndeterminateForTesting, "while the server starts the bar is indeterminate, not a made-up percentage");
            var progressWaiting = manager.Progress;
            entry.ProgressForTesting("snapshot", 1, 1); // arrives before the connection is even open
            Require(manager.Step("env")!.State == StepState.Pending && Mathf.IsEqualApprox((float)manager.Progress, (float)progressWaiting), "a fact that comes early waits for its turn");
            entry.StateForTesting("waiting_island", "Iniciando a ilha"); entry.StateForTesting("connecting", "c"); entry.StateForTesting("authenticating", "a");
            Require(manager.Step("server")!.State == StepState.Completed && manager.Step("connect")!.State == StepState.Running, "connecting follows the server");
            entry.StateForTesting("online", "Online");
            Require(manager.Step("connect")!.State == StepState.Completed && manager.Step("env")!.Done == 1 && manager.Step("env")!.Total == 2, "once online the early snapshot counts: 1 of 2 parts of the environment");
            entry.ProgressForTesting("objects", 0, 3); entry.ProgressForTesting("objects", 2, 3);
            Require(manager.Step("objects")!.Done == 2 && manager.Step("objects")!.Total == 3 && manager.Progress < 1, "objects report real counts");
            entry.ProgressForTesting("assets", 500, 2000); Require(manager.Step("assets")!.Done == 500 && manager.Step("assets")!.Total == 2000 && !manager.Step("assets")!.Indeterminate, "assets report real bytes");
            entry.ProgressForTesting("terrain", 1, 1); entry.ProgressForTesting("objects", 3, 3); Require(manager.Step("assets")!.State == StepState.Running, "assets are not done until all the bytes are");
            entry.ProgressForTesting("assets", 2000, 2000);
            Require(manager.Step("env")!.State == StepState.Completed && manager.Step("objects")!.State == StepState.Completed && manager.Step("avatar")!.State == StepState.Running && manager.Progress < 1, "everything but the avatar is ready, and the world is not entered yet");
            entry.ProgressForTesting("spawn", 1, 1);
            Require(manager.Step("ready")!.State == StepState.Running && !manager.Succeeded && manager.Progress < 1, "the last step is only finished a frame later");
            await Frames(2);
            Require(manager.Succeeded && Mathf.IsEqualApprox((float)manager.Progress, 1f), "the loading is complete only when every step is");
            await Frames(60);
            Require(!screen.IsOpen && screen.RootAlphaForTesting <= .01f, "the screen fades away when the world is ready");

            // a failure while the server starts: retry yes, going back no (this is not a trip from another island)
            entry.BeginForTesting(LoadingMode.Entry, "Tropical Island"); await Frames(1);
            entry.SetIslandForTesting(Guid.NewGuid(), "Tropical Island"); manager.Complete("locate"); entry.StateForTesting("waiting_island", "...");
            entry.StateForTesting("failed", "A ilha não iniciou a tempo."); await Frames(2);
            Require(manager.Failed && screen.ErrorForTesting.Contains("Tropical Island") && screen.ErrorForTesting.Contains("A ilha não iniciou a tempo."), $"the error says what happened: {screen.ErrorForTesting}");
            Require(screen.RetryVisibleForTesting && !screen.BackVisibleForTesting, "only the actions that are possible are offered");
            Require(manager.Step("server")!.State == StepState.Failed && manager.Step("connect")!.State == StepState.Cancelled, "the failed step is marked and the rest is cancelled");
            entry.LeaveForTesting(); await Frames(1);
            Require(left == 1 && !screen.IsOpen && manager.Finished, "going back to the menu closes the loading");

            // an expired session cannot be retried
            session.Clear(); entry.BeginLocating(); await Frames(2);
            Require(manager.Failed && manager.FailedStep!.Id == "session" && !screen.RetryVisibleForTesting, "an expired session offers no retry");
            entry.LeaveForTesting(); session.Set("smoke-token", "smoke-refresh", "smoke");

            // a trip: other title and steps, and a world that never finishes loading is reported
            entry.BeginForTesting(LoadingMode.Teleport, "Praia Azul"); await Frames(2);
            Require(screen.TitleForTesting == "Viajando para outra ilha..." && manager.Steps.Any(s => s.Name == "Transferindo sessão") && manager.Steps.Any(s => s.Name == "Posicionando avatar"), "a trip has its own title and steps");
            entry.SetIslandForTesting(Guid.NewGuid(), "Praia Azul"); manager.Complete("locate"); entry.StateForTesting("connecting", "c"); entry.StateForTesting("online", "ok");
            Require(manager.Step("auth")!.State == StepState.Completed, "the session transfer step follows the authentication");
            entry.WaitForTesting(WorldEntryCoordinator.WorldReadyTimeoutSeconds + 1);
            Require(manager.Failed && screen.ErrorForTesting.Contains("tempo"), "a world that never arrives times out");
            entry.RetryForTesting(); await Frames(1);
            Require(manager.Active && !manager.Failed, "retry runs the loading again");
            entry.LeaveForTesting();

            // the bar eases towards the real progress and never passes it
            entry.BeginForTesting(LoadingMode.Entry, "Ilha"); await Frames(30);
            Require(screen.DisplayedProgress <= manager.Progress + 1e-6, "the bar never gets ahead of the real progress");
            entry.LeaveForTesting();

            GD.Print("event=loading_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=loading_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
