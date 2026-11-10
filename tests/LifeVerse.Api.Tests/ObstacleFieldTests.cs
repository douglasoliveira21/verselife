using System.Numerics;
using LifeVerse.Shared.Networking;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class ObstacleFieldTests
{
    private const float Dt = 1f / 60f;
    private static AuthoritativePlayerState Player(float x = 0, float y = 0, float z = 0) => new() { X = x, Y = y, Z = z };

    private static ObstacleField Field(params (string Type, Vector3 Position, Vector3 Degrees, Vector3 Scale)[] items)
    {
        var field = new ObstacleField();
        foreach (var item in items) field.Set(Guid.NewGuid(), ObstacleField.For(item.Type, item.Position, item.Degrees, item.Scale));
        return field;
    }

    private static void Walk(AuthoritativePlayerState state, ObstacleField field, int steps, float dx = 1, float dz = 0, InputFlags flags = InputFlags.Run, float dt = Dt)
    {
        for (var i = 0; i < steps; i++) AuthoritativeMovement.Step(state, new MovementInput((uint)i, dx, dz, flags), dt, null, float.NegativeInfinity, float.PositiveInfinity, field);
    }

    [Fact]
    public void AWallStopsTheAvatarAtItsFace_AndKeepsItThereNoMatterHowLongItPushes()
    {
        var wall = Field(("cube", new Vector3(5, 1.5f, 0), Vector3.Zero, new Vector3(.5f, 3, 10)));
        var state = Player();
        Walk(state, wall, 600);
        var face = 5 - .25f; // the wall is half a metre thick
        Assert.InRange(state.X, face - ObstacleField.Radius - .03f, face - ObstacleField.Radius + .001f);
        Walk(state, wall, 600); // keeps forcing against it
        Assert.True(state.X < face - ObstacleField.Radius + .001f, $"went through: {state.X}");
        Assert.Equal(0f, state.VelocityX, 3);
    }

    [Fact]
    public void AThinWallCannotBeJumpedOverByAFastOrSlowFrame()
    {
        var thin = Field(("cube", new Vector3(4, 1.5f, 0), Vector3.Zero, new Vector3(.05f, 3, 10)));
        foreach (var dt in new[] { 1f / 120f, 1f / 60f, 1f / 20f, .1f })
        {
            var state = Player();
            Walk(state, thin, (int)(4f / dt), dt: dt);
            Assert.True(state.X < 4, $"dt {dt}: went through to {state.X}");
        }
    }

    [Fact]
    public void WallsSlideYouAlongThemInsteadOfSticking()
    {
        var wall = Field(("cube", new Vector3(3, 1.5f, 0), Vector3.Zero, new Vector3(.5f, 3, 20)));
        var state = Player();
        var furthest = 0f;
        for (var i = 0; i < 150 && state.Z < 8; i++) { AuthoritativeMovement.Step(state, new MovementInput((uint)i, 1, 1, InputFlags.Run), Dt, null, float.NegativeInfinity, float.PositiveInfinity, wall); furthest = MathF.Max(furthest, state.X); }
        Assert.True(state.Z > 5, $"must keep moving along the wall: z = {state.Z}");
        Assert.True(furthest < 3 - .25f, $"but never through it: x = {furthest}");
    }

    [Fact]
    public void ALowStepIsWalkedOntoAndStoodOn()
    {
        var step = Field(("cube", new Vector3(2, .15f, 0), Vector3.Zero, new Vector3(2, .3f, 4))); // top at 0.3, from x = 1 to 3
        var state = Player();
        float? onTop = null;
        for (var i = 0; i < 180; i++)
        {
            AuthoritativeMovement.Step(state, new MovementInput((uint)i, 1, 0, InputFlags.None), Dt, null, float.NegativeInfinity, float.PositiveInfinity, step);
            if (state.X is > 1.5f and < 2.5f) onTop = state.Y;
        }
        Assert.Equal(.3f, onTop!.Value, 2);
        Assert.Equal(0f, state.Y, 2); // and down again on the other side
    }

    [Fact]
    public void AHighWallIsNotASteps()
    {
        var wall = Field(("cube", new Vector3(3, .5f, 0), Vector3.Zero, new Vector3(1, 1, 6))); // top at 1.0, walking cannot climb it
        var state = Player();
        Walk(state, wall, 300, flags: InputFlags.None);
        Assert.Equal(0f, state.Y, 2);
        Assert.True(state.X < 3 - .5f - ObstacleField.Radius + .01f);
    }

    [Fact]
    public void ABoxCanBeJumpedOnto()
    {
        var box = Field(("cube", new Vector3(1.5f, .5f, 0), Vector3.Zero, new Vector3(2, 1, 2))); // top at 1.0, within a jump
        var state = Player(-1f);
        for (var i = 0; i < 240; i++)
            AuthoritativeMovement.Step(state, new MovementInput((uint)i, 1, 0, i < 3 ? InputFlags.Jump | InputFlags.Run : InputFlags.Run), Dt, null, float.NegativeInfinity, float.PositiveInfinity, box);
        // it ended up on top of the box, or against its wall when the jump fell short: it is never inside it
        Assert.True(state.Y >= 0f);
        Assert.False(state.X > .5f && state.X < 2.5f && state.Y < .95f, $"inside the box at ({state.X}, {state.Y})");
    }

    [Fact]
    public void FallingOntoABoxLandsOnTopAndStaysAtThatHeightWithoutTrembling()
    {
        var box = Field(("cube", new Vector3(0, .5f, 0), Vector3.Zero, new Vector3(3, 1, 3)));
        var state = Player(0, 4, 0);
        var heights = new List<float>();
        for (var i = 0; i < 300; i++) { AuthoritativeMovement.Step(state, new MovementInput((uint)i, 0, 0, InputFlags.None), Dt, null, float.NegativeInfinity, float.PositiveInfinity, box); if (i > 150) heights.Add(state.Y); }
        Assert.Equal(1f, state.Y, 3); Assert.True(state.Grounded);
        Assert.Equal(heights.Min(), heights.Max(), 5); // not a hair of movement: this is what shook the camera
    }

    [Fact]
    public void WalkingOffTheEdgeOfABoxDropsToTheTerrain()
    {
        var box = Field(("cube", new Vector3(0, .5f, 0), Vector3.Zero, new Vector3(2, 1, 2)));
        var state = Player(0, 1, 0);
        Walk(state, box, 240, flags: InputFlags.None);
        Assert.Equal(0f, state.Y, 2); Assert.True(state.X > 1.35f);
    }

    [Fact]
    public void ACeilingStopsAJump()
    {
        var ceiling = Field(("cube", new Vector3(0, 2.3f, 0), Vector3.Zero, new Vector3(4, .2f, 4))); // underside at 2.2: head just fits standing, not jumping
        var state = Player();
        var apex = 0f;
        for (var i = 0; i < 90; i++) { AuthoritativeMovement.Step(state, new MovementInput((uint)i, 0, 0, i < 2 ? InputFlags.Jump : InputFlags.None), Dt, null, float.NegativeInfinity, float.PositiveInfinity, ceiling); apex = MathF.Max(apex, state.Y); }
        Assert.True(apex < .5f, $"head went through the ceiling: apex {apex}");
    }

    [Fact]
    public void ARotatedWallStillBlocks()
    {
        var diamond = Field(("cube", new Vector3(4, 1.5f, 0), new Vector3(0, 45, 0), new Vector3(.5f, 3, 12)));
        var state = Player();
        var furthest = 0f;
        for (var i = 0; i < 90; i++) { AuthoritativeMovement.Step(state, new MovementInput((uint)i, 1, 0, InputFlags.Run), Dt, null, float.NegativeInfinity, float.PositiveInfinity, diamond); furthest = MathF.Max(furthest, state.X - state.Z); } // the wall's plane is x - z = 4
        Assert.True(furthest < 3.2f, $"walked through a turned wall: {furthest}");
    }

    [Fact]
    public void RoundThingsAreRoundNotSquare()
    {
        var pillar = Field(("cylinder", new Vector3(3, 1, 0), Vector3.Zero, new Vector3(1, 2, 1)));
        var aside = Player(0, 0, .6f); // passes beside the pillar's edge: a square would have stopped it
        Walk(aside, pillar, 200, flags: InputFlags.None);
        Assert.True(aside.X > 3.5f, $"a pillar must be walked around: {aside.X}");
        var ball = Field(("sphere", new Vector3(3, 1, 0), Vector3.Zero, new Vector3(2, 2, 2)));
        var head = Player();
        Walk(head, ball, 400);
        Assert.True(head.X < 3 - 1 - ObstacleField.Radius + .06f, $"a ball must stop the avatar: {head.X}");
    }

    [Fact]
    public void GhostsAndTriggersDoNotBlock()
    {
        Assert.False(ObstacleField.Blocks("{\"collision\":false}")); Assert.False(ObstacleField.Blocks("{\"trigger\":true}"));
        Assert.True(ObstacleField.Blocks("{\"collision\":true}")); Assert.True(ObstacleField.Blocks("{}")); Assert.True(ObstacleField.Blocks("not json")); Assert.True(ObstacleField.Blocks(null));
    }

    [Fact]
    public void TheFieldFollowsObjectsThatMoveAndAreDeleted()
    {
        var field = new ObstacleField(); var id = Guid.NewGuid();
        field.Set(id, ObstacleField.For("cube", new Vector3(3, 1.5f, 0), Vector3.Zero, new Vector3(.5f, 3, 10)));
        var blocked = Player(); Walk(blocked, field, 400);
        Assert.True(blocked.X < 3);
        field.Set(id, ObstacleField.For("cube", new Vector3(30, 1.5f, 0), Vector3.Zero, new Vector3(.5f, 3, 10))); // moved far away
        var free = Player(); Walk(free, field, 400);
        Assert.True(free.X > 6);
        field.Remove(id); Assert.Equal(0, field.Count);
    }

    [Fact]
    public void WithoutObstaclesTheMovementIsExactlyAsBefore()
    {
        var a = Player(); var b = Player();
        for (var i = 0; i < 300; i++)
        {
            var input = new MovementInput((uint)i, 1, .3f, i % 90 == 0 ? InputFlags.Jump | InputFlags.Run : InputFlags.Run);
            AuthoritativeMovement.Step(a, input, Dt);
            AuthoritativeMovement.Step(b, input, Dt, null, float.NegativeInfinity, float.PositiveInfinity, new ObstacleField());
        }
        Assert.Equal((a.X, a.Y, a.Z), (b.X, b.Y, b.Z));
    }
}

public sealed class MeshObstacleTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData("{}", false)]
    [InlineData("{\"shape\":\"triangle\"}", false)]
    [InlineData("{\"shape\":\"box\"}", true)]
    [InlineData("{\"shape\":\"convex\"}", true)]
    [InlineData("{\"shape\":\"box\",\"trigger\":true}", false)]
    public void AnImportedModelIsOnlyAWallForTheServerWhenAnApproximateShapeWasChosen(string physics, bool blocks) =>
        Assert.Equal(blocks, ObstacleField.Blocks(physics, "mesh"));

    [Fact]
    public void PrimitivesKeepBlockingByDefault() => Assert.True(ObstacleField.Blocks("{}", "cube") && ObstacleField.Blocks("", "sphere"));
}
