using LifeVerse.Shared.Networking;
using LifeVerse.Shared.Terrain;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class TerrainMapTests
{
    [Fact]
    public void NewTerrain_IsFlatInsideWithABeachSlopingIntoTheSea()
    {
        var map = new TerrainMap();
        Assert.Equal(0f, map.SampleHeight(0, 0));
        Assert.Equal(TerrainMap.MinHeight, map.SampleHeight(map.HalfExtent, -map.HalfExtent));
        Assert.Equal(0f, map.SampleHeight(map.HalfExtent - TerrainMap.ShoreWidth - 2, 0));
        Assert.Equal(255, map.SplatAt(10, 10, 0));
        Assert.Equal(0, map.SplatAt(10, 10, 1));
    }

    [Fact]
    public void RaiseLowerFlattenAndSmooth_ReshapeTheGround()
    {
        var map = new TerrainMap();
        for (var i = 0; i < 10; i++) map.Apply(TerrainTool.Raise, 0, 0, 6, .4f);
        var peak = map.SampleHeight(0, 0);
        Assert.InRange(peak, 3.9f, 4.1f);
        Assert.True(map.SampleHeight(4, 0) < peak);
        Assert.Equal(0f, map.SampleHeight(20, 20));

        map.Apply(TerrainTool.Lower, 0, 0, 6, 1f);
        Assert.True(map.SampleHeight(0, 0) < peak);

        var before = map.SampleHeight(2, 0);
        map.Apply(TerrainTool.Smooth, 2, 0, 6, 1f);
        Assert.NotEqual(before, map.SampleHeight(2, 0));

        for (var i = 0; i < 30; i++) map.Apply(TerrainTool.Flatten, 0, 0, 10, 1f);
        Assert.InRange(MathF.Abs(map.SampleHeight(3, 3) - map.SampleHeight(0, 0)), 0f, .05f);
    }

    [Fact]
    public void Heights_AreClampedToTheAllowedRange()
    {
        var map = new TerrainMap();
        for (var i = 0; i < 400; i++) map.Apply(TerrainTool.Raise, 0, 0, 4, 2f);
        Assert.Equal(TerrainMap.MaxHeight, map.SampleHeight(0, 0));
        for (var i = 0; i < 800; i++) map.Apply(TerrainTool.Lower, 0, 0, 4, 2f);
        Assert.Equal(TerrainMap.MinHeight, map.SampleHeight(0, 0));
    }

    [Fact]
    public void Paint_KeepsWeightsSummingToFullAndSwitchesLayer()
    {
        var map = new TerrainMap();
        map.Apply(TerrainTool.Paint, 0, 0, 6, .5f, 2);
        var x = map.Cells / 2; var z = map.Cells / 2;
        Assert.Equal(255, Enumerable.Range(0, TerrainMap.LayerCount).Sum(layer => map.SplatAt(x, z, layer)));
        Assert.True(map.SplatAt(x, z, 2) > 100);
        for (var i = 0; i < 6; i++) map.Apply(TerrainTool.Paint, 0, 0, 6, 1f, 3);
        Assert.Equal(255, map.SplatAt(x, z, 3));
        Assert.Equal(0, map.SplatAt(x, z, 0));
    }

    [Fact]
    public void SerializeAndJson_RoundTripExactly()
    {
        var map = new TerrainMap();
        map.Apply(TerrainTool.Raise, 10, -12, 8, .9f);
        map.Apply(TerrainTool.Paint, -20, 5, 5, 1f, 1);
        map.Apply(TerrainTool.Noise, 0, 0, 8, .5f);

        foreach (var copy in new[] { TerrainMap.FromJson(map.ToJson()), TerrainMap.TryDeserialize(map.Serialize(), out var direct) ? direct : throw new Xunit.Sdk.XunitException("deserialize failed") })
        {
            Assert.Equal(map.Revision, copy.Revision);
            Assert.Equal(map.Heights, copy.Heights);
            Assert.Equal(map.Splat, copy.Splat);
        }
    }

    [Fact]
    public void ReplayingAcceptedEdits_ProducesTheSameTerrainOnAnotherCopy()
    {
        var server = new TerrainMap();
        var client = TerrainMap.FromJson(server.ToJson());
        var edits = new (TerrainTool Tool, float X, float Z, float Radius, float Strength, int Layer)[]
        {
            (TerrainTool.Raise, 5, 5, 6, .4f, 0), (TerrainTool.Smooth, 6, 6, 5, .8f, 0), (TerrainTool.Flatten, 3, 4, 7, .6f, 0),
            (TerrainTool.Paint, 8, 8, 4, .7f, 2), (TerrainTool.Noise, -5, 2, 6, .3f, 0)
        };
        foreach (var edit in edits)
        {
            server.Apply(edit.Tool, edit.X, edit.Z, edit.Radius, edit.Strength, edit.Layer);
            client.Apply(edit.Tool, edit.X, edit.Z, edit.Radius, edit.Strength, edit.Layer);
        }
        Assert.Equal(server.Revision, client.Revision);
        Assert.Equal(server.Heights, client.Heights);
        Assert.Equal(server.Splat, client.Splat);
    }

    [Fact]
    public void InvalidInput_IsRejectedWithoutChangingTheTerrain()
    {
        var map = new TerrainMap();
        Assert.False(map.Apply(TerrainTool.Raise, float.NaN, 0, 4, 1));
        Assert.False(map.Apply(TerrainTool.Raise, 0, 0, 4, float.PositiveInfinity));
        Assert.False(map.Apply((TerrainTool)99, 0, 0, 4, 1));
        Assert.Equal(0, map.Revision);
        Assert.False(TerrainMap.TryDeserialize([1, 2, 3], out _));
        Assert.False(TerrainMap.TryDeserialize(new byte[64], out _));
        Assert.Equal(TerrainMap.DefaultCells, TerrainMap.FromJson("{}").Cells);
        Assert.Equal(TerrainMap.DefaultCells, TerrainMap.FromJson("not json").Cells);
    }

    [Fact]
    public void Avatar_FollowsTheTerrainUpAndDownHills()
    {
        var map = new TerrainMap();
        for (var i = 0; i < 12; i++) map.Apply(TerrainTool.Raise, 0, 12, 10, .4f);
        var state = new AuthoritativePlayerState { Z = -4, Y = AuthoritativeMovement.GroundOffset };
        var highest = state.Y;
        for (var i = 0; i < 600; i++)
        {
            AuthoritativeMovement.Step(state, new MovementInput((uint)i, 0, 1, InputFlags.None), 1f / 60f, map.SampleHeight);
            highest = MathF.Max(highest, state.Y);
            Assert.InRange(state.Y, map.SampleHeight(state.X, state.Z) - .001f, map.SampleHeight(state.X, state.Z) + .5f);
        }
        Assert.True(highest > 3f);
        Assert.True(state.Z > 12f);

        var motion = AuthoritativeMovement.Step(state, new MovementInput(9999, 0, 0, InputFlags.None), 1f / 60f, map.SampleHeight);
        Assert.True(state.Grounded);
        Assert.NotEqual(NetworkMotionState.Fall, motion);
    }

    [Fact]
    public void Avatar_StandingOnTerrainThatRisesIsLiftedToTheSurface()
    {
        var map = new TerrainMap();
        var state = new AuthoritativePlayerState { Y = AuthoritativeMovement.GroundOffset };
        for (var i = 0; i < 20; i++) map.Apply(TerrainTool.Raise, 0, 0, 5, .5f);
        AuthoritativeMovement.Step(state, new MovementInput(1, 0, 0, InputFlags.None), 1f / 60f, map.SampleHeight);
        Assert.InRange(state.Y, map.SampleHeight(0, 0) - .001f, map.SampleHeight(0, 0) + .001f);
    }
}

public sealed class OceanTests
{
    private static readonly Func<float, float, float> Ground = new TerrainMap().SampleHeight;
    private static readonly float Limit = new TerrainMap().HalfExtent + TerrainMap.OceanMargin;

    [Fact]
    public void Shore_SlopesFromLandThroughTheWaterlineToTheSeaFloor()
    {
        var map = new TerrainMap();
        Assert.True(map.SampleHeight(map.HalfExtent - 2, 0) < TerrainMap.SeaLevel - AuthoritativeMovement.WadeDepth);
        Assert.True(map.SampleHeight(map.HalfExtent - ShoreMid(), 0) is > TerrainMap.MinHeight and < 0f);
        Assert.Equal(TerrainMap.MinHeight, map.SampleHeight(map.HalfExtent + 100, 0));
        Assert.True(map.SplatAt(map.Cells - 2, map.Cells / 2, 2) > 200); // sandy beach
        Assert.Equal(255, map.SplatAt(map.Cells / 2, map.Cells / 2, 0)); // grassy interior
    }

    [Fact]
    public void SeaFloorRing_CannotBeSculpted()
    {
        var map = new TerrainMap();
        map.Apply(TerrainTool.Raise, map.HalfExtent, 0, 12, 1f);
        map.Apply(TerrainTool.Flatten, map.HalfExtent, 0, 12, 1f);
        Assert.Equal(TerrainMap.MinHeight, map.SampleHeight(map.HalfExtent, 0));
        Assert.Equal(TerrainMap.MinHeight, map.HeightAt(map.Cells, map.Cells / 2));
    }

    [Fact]
    public void Avatar_WalksIntoTheSeaAndSwimsAtTheSurface()
    {
        var state = new AuthoritativePlayerState { X = 60 };
        var motion = NetworkMotionState.Idle;
        for (var i = 0; i < 1200 && !state.Swimming; i++)
            motion = AuthoritativeMovement.Step(state, new MovementInput((uint)i, 1, 0, InputFlags.None), 1f / 60f, Ground, TerrainMap.SeaLevel, Limit);
        Assert.True(state.Swimming);
        Assert.Equal(NetworkMotionState.Swim, motion);
        for (var i = 0; i < 600; i++)
            motion = AuthoritativeMovement.Step(state, new MovementInput((uint)i, 1, 0, InputFlags.None), 1f / 60f, Ground, TerrainMap.SeaLevel, Limit);
        var floatY = TerrainMap.SeaLevel - AuthoritativeMovement.SwimFloatDepth;
        Assert.InRange(state.Y, floatY - .15f, floatY + .15f);
        Assert.True(state.Swimming);
    }

    [Fact]
    public void Swimming_JumpRisesToTheSurfaceAndDescendDivesToTheSeaFloor()
    {
        var state = new AuthoritativePlayerState { X = 140, Y = TerrainMap.SeaLevel - AuthoritativeMovement.SwimFloatDepth };
        for (var i = 0; i < 240; i++) AuthoritativeMovement.Step(state, new MovementInput((uint)i, 0, 0, InputFlags.Jump), 1f / 60f, Ground, TerrainMap.SeaLevel, Limit);
        Assert.InRange(state.Y, TerrainMap.SeaLevel - AuthoritativeMovement.SwimFloatDepth, TerrainMap.SeaLevel - AuthoritativeMovement.SwimFloatDepth + AuthoritativeMovement.SwimSurfaceMargin + .001f);
        for (var i = 0; i < 600; i++) AuthoritativeMovement.Step(state, new MovementInput((uint)i, 0, 0, InputFlags.Descend), 1f / 60f, Ground, TerrainMap.SeaLevel, Limit);
        Assert.InRange(state.Y, TerrainMap.MinHeight, TerrainMap.MinHeight + .01f);
    }

    [Fact]
    public void Swimming_IsSlowerThanRunningAndStopsAtTheWorldLimit()
    {
        var state = new AuthoritativePlayerState { X = 200, Y = TerrainMap.SeaLevel - AuthoritativeMovement.SwimFloatDepth };
        for (var i = 0; i < 120; i++) AuthoritativeMovement.Step(state, new MovementInput((uint)i, 1, 0, InputFlags.Run), 1f / 60f, Ground, TerrainMap.SeaLevel, Limit);
        Assert.InRange(MathF.Abs(state.VelocityX), AuthoritativeMovement.SwimRunSpeed - .01f, AuthoritativeMovement.SwimRunSpeed + .01f);
        for (var i = 0; i < 6000; i++) AuthoritativeMovement.Step(state, new MovementInput((uint)i, 1, 0, InputFlags.Run), 1f / 60f, Ground, TerrainMap.SeaLevel, Limit);
        Assert.Equal(Limit, state.X);
    }

    [Fact]
    public void Flying_OverTheOceanIsNotSwimming()
    {
        var state = new AuthoritativePlayerState { X = 150, Y = 5, Flying = true };
        var motion = AuthoritativeMovement.Step(state, new MovementInput(1, 0, 0, InputFlags.None), 1f / 60f, Ground, TerrainMap.SeaLevel, Limit);
        Assert.False(state.Swimming);
        Assert.Equal(NetworkMotionState.Fly, motion);
    }

    private static float ShoreMid() => TerrainMap.ShoreWidth / 2f;
}
