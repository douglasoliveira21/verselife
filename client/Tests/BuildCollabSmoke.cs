using System.Text.Json;
using Godot;
using LifeVerse.Client.Build;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>The new editor, phase 9: building together. Leases follow the selection, the leases of other players block edits, their drags are shown live and undone when dropped, and a reconnection asks for the leases again.</summary>
public partial class BuildCollabSmoke : Node3D
{
    private static string StateJson(Guid id, int revision, float x = 0, string operation = "update") =>
        JsonSerializer.Serialize(new { operation, objectState = new { id, islandId = Guid.NewGuid(), creatorUserId = Guid.Empty, ownerUserId = Guid.Empty, name = "Caixa", description = "", primitiveType = "cube", positionX = x, positionY = .5f, positionZ = 0, rotationX = 0, rotationY = 0, rotationZ = 0, scaleX = 1, scaleY = 1, scaleZ = 1, materialJson = "{}", geometryJson = "{}", physicsJson = "{\"collision\":true}", permissionsJson = "{}", contentJson = "[]", revision, cost = 2 } });

    private static string LockJson(string operation, Guid user, string name, long expires, params Guid[] ids) =>
        JsonSerializer.Serialize(new { operation, objectIds = ids, userId = user, userName = name, expiresAtMs = expires });

    private static string Op(string command) => JsonDocument.Parse(command).RootElement.GetProperty("operation").GetString()!;

    private static string Live(Guid user, Guid id, float x) =>
        JsonSerializer.Serialize(new { operation = "live", userId = user, poses = new[] { new { id, positionX = x, positionY = .5f, positionZ = 1f, rotationX = 0f, rotationY = 90f, rotationZ = 0f, scaleX = 1f, scaleY = 1f, scaleZ = 1f } } });

    public override async void _Ready()
    {
        try
        {
            var camera = new Camera3D { Current = true, Position = new Vector3(0, 8, 8) }; AddChild(camera); camera.LookAt(Vector3.Zero);
            var editor = new BuildEditorController { Name = "BuildEditorController" }; AddChild(editor);
            var commands = new List<string>(); editor.Synchronizer.OperationSink = json => { commands.Add(json); return true; };
            var rejections = new List<string>(); editor.Synchronizer.Rejected += text => rejections.Add(text);
            await Frames(3);
            var a = Guid.NewGuid(); var b = Guid.NewGuid(); var maria = Guid.NewGuid(); var pedro = Guid.NewGuid();
            editor.Synchronizer.OnOperation(StateJson(a, 1, 2, "create")); editor.Synchronizer.OnOperation(StateJson(b, 1, -2, "create")); await Frames(2);
            var locks = editor.Locks; var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // --- my leases follow my selection ------------------------------------------------------------------------
            editor.SetActive(true); commands.Clear(); editor.Selection.Set([a]); await Frames(2);
            Require(commands.Count == 1 && Op(commands[0]) == "lock" && commands[0].Contains(a.ToString()), "selecting an object asks for its lease");
            commands.Clear(); editor.Selection.Set([b]); await Frames(2);
            Require(commands.Any(c => Op(c) == "unlock" && c.Contains(a.ToString())) && commands.Any(c => Op(c) == "lock" && c.Contains(b.ToString())), "changing the selection gives one back and takes the other");
            commands.Clear(); locks.Renew(); Require(commands.Count == 1 && Op(commands[0]) == "lock", "the lease is renewed while I work");

            // --- somebody else holds A ------------------------------------------------------------------------------------
            editor.Synchronizer.OnOperation(LockJson("lock", maria, "Maria", now + 20_000, a)); await Frames(2);
            Require(locks.LockedByOther(a, out var who) && who == "Maria", "the lease of another player is known");
            commands.Clear(); rejections.Clear(); editor.Selection.Set([a]); await Frames(2);
            Require(!commands.Any(c => Op(c) == "lock" && c.Contains(a.ToString())), "I do not ask for an object somebody else holds");
            var edited = BuildPersistenceManager.Clone(editor.Synchronizer.Views[a].State); edited.PositionX = 5;
            commands.Clear(); Require(!editor.Synchronizer.Update(edited) && commands.Count == 0, "an edit of a held object is refused before it is sent");
            Require(rejections.Any(r => r.Contains("Maria")), "and the refusal names who is editing");

            // --- her drag is shown live, only if she really holds it ----------------------------------------------------
            editor.Synchronizer.OnOperation(Live(maria, a, 4f)); await Frames(1);
            Require(Mathf.IsEqualApprox(editor.Synchronizer.Views[a].Position.X, 4f) && Mathf.IsEqualApprox(editor.Synchronizer.Views[a].Position.Z, 1f), "the others see the object move as she drags it");
            editor.Synchronizer.OnOperation(Live(pedro, a, 9f)); await Frames(1);
            Require(Mathf.IsEqualApprox(editor.Synchronizer.Views[a].Position.X, 4f), "a drag from somebody who does not hold it is ignored");
            editor.Synchronizer.OnOperation(LockJson("unlock", maria, "Maria", 0, a)); await Frames(2);
            Require(!locks.LockedByOther(a, out _) && Mathf.IsEqualApprox(editor.Synchronizer.Views[a].Position.X, 2f), "a drag dropped without saving puts the object back where it really is");
            editor.Synchronizer.OnOperation(LockJson("lock", maria, "Maria", now + 20_000, a));
            editor.Synchronizer.OnOperation(Live(maria, a, 6f)); editor.Synchronizer.OnOperation(StateJson(a, 2, 6f)); editor.Synchronizer.OnOperation(LockJson("unlock", maria, "Maria", 0, a)); await Frames(2);
            Require(Mathf.IsEqualApprox(editor.Synchronizer.Views[a].Position.X, 6f), "a drag she saved stays");

            // --- my own drag is streamed -------------------------------------------------------------------------------------
            editor.Selection.Set([b]); await Frames(2); locks.Renew();
            commands.Clear(); locks.Stream([editor.Synchronizer.Views[b], editor.Synchronizer.Views[a]], 1.0);
            Require(commands.Count == 1 && Op(commands[0]) == "live" && commands[0].Contains(b.ToString()) && !commands[0].Contains(a.ToString()), "my drag is streamed, only for objects I hold");

            // --- expiry and reconnection ------------------------------------------------------------------------------
            editor.Synchronizer.OnOperation(LockJson("lock", maria, "Maria", now + 20_000, a));
            Require(locks.LockedByOther(a, out _), "held again");
            locks.Clock = () => now + 60_000; Require(!locks.LockedByOther(a, out _), "a lease that was not renewed expires by itself"); locks.Clock = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            editor.Synchronizer.OnOperation(LockJson("lock", maria, "Maria", now + 20_000, a));
            commands.Clear(); editor.Synchronizer.ReconnectedForTesting();
            Require(!locks.LockedByOther(a, out _), "after a reconnection no lease of anybody else is assumed");
            Require(commands.Any(c => Op(c) == "lock" && c.Contains(b.ToString())), "and my leases are asked for again");

            // --- leaving gives everything back -------------------------------------------------------------------------
            commands.Clear(); editor.SetActive(false);
            Require(commands.Any(c => Op(c) == "unlock" && c.Contains(b.ToString())) && locks.Mine.Count == 0, "closing the editor gives the leases back");

            // --- history text --------------------------------------------------------------------------------------------
            var text = BuildAuditManager.Format("[{\"actorUserId\":\"" + maria + "\",\"operation\":\"update\",\"revision\":3,\"at\":\"2026-11-10T12:00:00Z\"},{\"actorUserId\":\"" + Guid.Empty + "\",\"operation\":\"create\",\"revision\":1,\"at\":\"2026-11-10T11:00:00Z\"}]", Guid.Empty);
            Require(text is not null && text.Contains("Alterado") && text.Contains("você") && text.Contains("revisão 3"), "the history lists what happened and who did it");
            Require(BuildAuditManager.Format("{}", Guid.Empty) is null, "an unexpected answer is not shown as history");

            GD.Print("event=build_collab_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=build_collab_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
