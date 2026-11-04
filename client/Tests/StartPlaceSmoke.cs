using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>The first island of a session: what the server says is read, the person is told where they are going, and which way they faced is remembered on this computer.</summary>
public partial class StartPlaceSmoke : Node
{
    public override void _Ready()
    {
        try
        {
            var island = Guid.NewGuid();
            var last = StartPlace.Parse($"{{\"islandId\":\"{island}\",\"name\":\"Floresta\",\"reason\":\"last\",\"isWelcome\":false}}");
            Require(last is { Name: "Floresta", Reason: "last", IsWelcome: false } && last.IslandId == island, "the island to go back to is read");
            Require(StartPlace.Message(last!) == "Voltando para Floresta, onde você estava.", $"the person is told: {StartPlace.Message(last!)}");
            var welcome = StartPlace.Parse($"{{\"islandId\":\"{island}\",\"name\":\"Ilha de Boas-vindas\",\"reason\":\"welcome\",\"isWelcome\":true}}");
            Require(welcome is { IsWelcome: true } && StartPlace.Message(welcome).StartsWith("Bem-vindo!"), "a new avatar is welcomed");
            Require(StartPlace.Parse("not json") is null && StartPlace.Parse("{}") is null && StartPlace.Parse($"{{\"islandId\":\"{Guid.Empty}\"}}") is null && StartPlace.Parse("{\"islandId\":\"x\"}") is null, "anything else is no island");
            Require(StartPlace.Parse($"{{\"islandId\":\"{island}\"}}") is { Reason: "welcome" }, "a missing reason is the welcome");

            StartPlace.FacingPath = "user://test_last_place.json";
            var me = Guid.NewGuid(); var other = Guid.NewGuid(); var elsewhere = Guid.NewGuid();
            Require(StartPlace.FacingOn(me, island) is null, "nothing is remembered at first");
            StartPlace.Remember(me, island, 1.25f);
            Require(StartPlace.FacingOn(me, island) is { } yaw && Mathf.Abs(yaw - 1.25f) < .001f, "the direction comes back for the same person on the same island");
            Require(StartPlace.FacingOn(other, island) is null && StartPlace.FacingOn(me, elsewhere) is null, "not for another person, nor for another island");
            StartPlace.Remember(me, island, float.NaN); StartPlace.Remember(Guid.Empty, island, 2f);
            Require(StartPlace.FacingOn(me, island) is { } kept && Mathf.Abs(kept - 1.25f) < .001f, "a broken value does not overwrite it");
            using (var file = Godot.FileAccess.Open(StartPlace.FacingPath, Godot.FileAccess.ModeFlags.Write)) file.StoreString("{broken");
            Require(StartPlace.FacingOn(me, island) is null, "a damaged file is just no memory");
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(StartPlace.FacingPath));

            GD.Print("event=start_place_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=start_place_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
