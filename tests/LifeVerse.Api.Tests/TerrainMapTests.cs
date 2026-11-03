using LifeVerse.Shared.Audio;
using LifeVerse.Shared.Building;
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
        Assert.True(map.SampleHeight(map.HalfExtent - 2, 0) < TerrainMap.DefaultSeaLevel - AuthoritativeMovement.WadeDepth);
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
            motion = AuthoritativeMovement.Step(state, new MovementInput((uint)i, 1, 0, InputFlags.None), 1f / 60f, Ground, TerrainMap.DefaultSeaLevel, Limit);
        Assert.True(state.Swimming);
        Assert.Equal(NetworkMotionState.Swim, motion);
        for (var i = 0; i < 600; i++)
            motion = AuthoritativeMovement.Step(state, new MovementInput((uint)i, 1, 0, InputFlags.None), 1f / 60f, Ground, TerrainMap.DefaultSeaLevel, Limit);
        var floatY = TerrainMap.DefaultSeaLevel - AuthoritativeMovement.SwimFloatDepth;
        Assert.InRange(state.Y, floatY - .15f, floatY + .15f);
        Assert.True(state.Swimming);
    }

    [Fact]
    public void Swimming_JumpRisesToTheSurfaceAndDescendDivesToTheSeaFloor()
    {
        var state = new AuthoritativePlayerState { X = 140, Y = TerrainMap.DefaultSeaLevel - AuthoritativeMovement.SwimFloatDepth };
        for (var i = 0; i < 240; i++) AuthoritativeMovement.Step(state, new MovementInput((uint)i, 0, 0, InputFlags.Jump), 1f / 60f, Ground, TerrainMap.DefaultSeaLevel, Limit);
        Assert.InRange(state.Y, TerrainMap.DefaultSeaLevel - AuthoritativeMovement.SwimFloatDepth, TerrainMap.DefaultSeaLevel - AuthoritativeMovement.SwimFloatDepth + AuthoritativeMovement.SwimSurfaceMargin + .001f);
        for (var i = 0; i < 600; i++) AuthoritativeMovement.Step(state, new MovementInput((uint)i, 0, 0, InputFlags.Descend), 1f / 60f, Ground, TerrainMap.DefaultSeaLevel, Limit);
        Assert.InRange(state.Y, TerrainMap.MinHeight, TerrainMap.MinHeight + .01f);
    }

    [Fact]
    public void Swimming_IsSlowerThanRunningAndStopsAtTheWorldLimit()
    {
        var state = new AuthoritativePlayerState { X = 200, Y = TerrainMap.DefaultSeaLevel - AuthoritativeMovement.SwimFloatDepth };
        for (var i = 0; i < 120; i++) AuthoritativeMovement.Step(state, new MovementInput((uint)i, 1, 0, InputFlags.Run), 1f / 60f, Ground, TerrainMap.DefaultSeaLevel, Limit);
        Assert.InRange(MathF.Abs(state.VelocityX), AuthoritativeMovement.SwimRunSpeed - .01f, AuthoritativeMovement.SwimRunSpeed + .01f);
        for (var i = 0; i < 6000; i++) AuthoritativeMovement.Step(state, new MovementInput((uint)i, 1, 0, InputFlags.Run), 1f / 60f, Ground, TerrainMap.DefaultSeaLevel, Limit);
        Assert.Equal(Limit, state.X);
    }

    [Fact]
    public void Flying_OverTheOceanIsNotSwimming()
    {
        var state = new AuthoritativePlayerState { X = 150, Y = 5, Flying = true };
        var motion = AuthoritativeMovement.Step(state, new MovementInput(1, 0, 0, InputFlags.None), 1f / 60f, Ground, TerrainMap.DefaultSeaLevel, Limit);
        Assert.False(state.Swimming);
        Assert.Equal(NetworkMotionState.Fly, motion);
    }

    private static float ShoreMid() => TerrainMap.ShoreWidth / 2f;
}

public sealed class TerrainHistoryTests
{
    [Fact]
    public void Undo_RestoresTheCapturedStateAndRedoReappliesIt()
    {
        var map = new TerrainMap();
        var original = map.Heights.ToArray();
        var before = map.Capture(10, 10, 8);
        map.Apply(TerrainTool.Raise, 10, 10, 8, .5f);
        map.Apply(TerrainTool.Paint, 10, 10, 8, 1f, 3);
        var edited = map.Heights.ToArray(); var editedSplat = map.Splat.ToArray();
        Assert.NotEqual(original, edited);

        var redo = map.Restore(before);
        Assert.Equal(original, map.Heights);
        Assert.Equal(255, map.SplatAt(map.Cells / 2 + 5, map.Cells / 2 + 5, 0));

        map.Restore(redo);
        Assert.Equal(edited, map.Heights);
        Assert.Equal(editedSplat, map.Splat);
    }

    [Fact]
    public void Stroke_MergeKeepsTheOldestValueOfEachVertex()
    {
        var map = new TerrainMap();
        var stroke = map.Capture(0, 0, 6);
        map.Apply(TerrainTool.Raise, 0, 0, 6, .5f);
        stroke.MergeKeepingOldest(map.Capture(1, 1, 6));
        map.Apply(TerrainTool.Raise, 1, 1, 6, .5f);
        map.Restore(stroke);
        Assert.Equal(0f, map.SampleHeight(0, 0));
        Assert.Equal(0f, map.SampleHeight(1, 1));
    }

    [Fact]
    public void Patch_RoundTripsAndRejectsOutOfRangeVertices()
    {
        var map = new TerrainMap();
        map.Apply(TerrainTool.Raise, 5, 5, 6, 1f);
        var patch = map.Capture(5, 5, 6);
        var count = map.VertexCount * map.VertexCount;
        Assert.True(TerrainPatch.TryDeserialize(patch.Serialize(), count, out var copy));
        Assert.Equal(patch.Count, copy.Count);

        var bad = new TerrainPatch(); bad.Add(count + 5, 1f, [255, 0, 0, 0]);
        Assert.False(TerrainPatch.TryDeserialize(bad.Serialize(), count, out _));
        Assert.False(TerrainPatch.TryDeserialize([1, 2, 3, 4, 5], count, out _));
    }

    [Fact]
    public void Restore_NeverChangesTheSeaFloorBorder()
    {
        var map = new TerrainMap();
        var patch = new TerrainPatch(); patch.Add(0, 30f, [255, 0, 0, 0]);
        map.Restore(patch);
        Assert.Equal(TerrainMap.MinHeight, map.HeightAt(0, 0));
    }

    [Fact]
    public void SeaLevel_ChangesClampsAndSurvivesSerialization()
    {
        var map = new TerrainMap();
        Assert.Equal(TerrainMap.DefaultSeaLevel, map.SeaLevel);
        Assert.True(map.SetSeaLevel(3.456f));
        Assert.Equal(3.46f, map.SeaLevel);
        Assert.False(map.SetSeaLevel(3.46f));
        map.SetSeaLevel(500f);
        Assert.Equal(TerrainMap.MaxSeaLevel, map.SeaLevel);
        map.SetSeaLevel(-500f);
        Assert.Equal(TerrainMap.MinSeaLevel, map.SeaLevel);
        Assert.False(map.SetSeaLevel(float.NaN));
        map.SetSeaLevel(2.5f);
        Assert.Equal(2.5f, TerrainMap.FromJson(map.ToJson()).SeaLevel);
        Assert.Equal(map.Revision, TerrainMap.FromJson(map.ToJson()).Revision);
    }

    [Fact]
    public void MapsSavedBeforeTheSeaLevelWasStoredStillLoad()
    {
        const int cells = 4; const int vertices = cells + 1;
        using var raw = new MemoryStream();
        using (var writer = new BinaryWriter(raw, System.Text.Encoding.UTF8, true))
        {
            writer.Write(0x3154564Cu); writer.Write(cells); writer.Write(2f); writer.Write(7L);
            for (var i = 0; i < vertices * vertices; i++) writer.Write((ushort)(8000 + 1500));
            for (var i = 0; i < vertices * vertices; i++) writer.Write(new byte[] { 255, 0, 0, 0 });
        }
        using var packed = new MemoryStream();
        using (var deflate = new System.IO.Compression.DeflateStream(packed, System.IO.Compression.CompressionLevel.Optimal, true)) raw.WriteTo(deflate);
        Assert.True(TerrainMap.TryDeserialize(packed.ToArray(), out var map));
        Assert.Equal(TerrainMap.DefaultSeaLevel, map.SeaLevel);
        Assert.Equal(7, map.Revision);
        Assert.Equal(1.5f, map.HeightAt(2, 2));
    }

    [Fact]
    public void HeightmapExportThenImport_ReproducesTheTerrainAndCanBeUndone()
    {
        var source = new TerrainMap();
        for (var i = 0; i < 6; i++) source.Apply(TerrainTool.Raise, 20, -10, 12, .6f);
        var bytes = source.ExportHeights16();
        Assert.Equal(source.VertexCount * source.VertexCount * 2, bytes.Length);

        var target = new TerrainMap();
        var undo = target.ImportHeights16(bytes);
        Assert.NotNull(undo);
        for (var z = 0; z < target.VertexCount; z += 7)
        for (var x = 0; x < target.VertexCount; x += 7)
            Assert.InRange(MathF.Abs(target.HeightAt(x, z) - source.HeightAt(x, z)), 0f, .002f);

        target.Restore(undo!);
        Assert.Equal(new TerrainMap().Heights, target.Heights);
    }

    [Fact]
    public void HeightmapImport_RejectsWrongSizesAndKeepsTheBorder()
    {
        var map = new TerrainMap();
        Assert.Null(map.ImportHeights16(new byte[10]));
        var allMax = Enumerable.Repeat((byte)255, map.VertexCount * map.VertexCount * 2).ToArray();
        Assert.NotNull(map.ImportHeights16(allMax));
        Assert.Equal(TerrainMap.MaxHeight, map.HeightAt(10, 10), .01f);
        Assert.Equal(TerrainMap.MinHeight, map.HeightAt(0, 10));
        Assert.Equal(TerrainMap.MinHeight, map.HeightAt(map.Cells, map.Cells));
    }

    [Fact]
    public void ReplayingPatchesAndSeaLevelKeepsServerAndClientIdentical()
    {
        var server = new TerrainMap(); var client = TerrainMap.FromJson(server.ToJson());
        var before = server.Capture(3, 3, 10);
        server.Apply(TerrainTool.Raise, 3, 3, 10, .8f); client.Apply(TerrainTool.Raise, 3, 3, 10, .8f);
        server.SetSeaLevel(1.2f); client.SetSeaLevel(1.2f);
        server.Restore(before);
        Assert.True(TerrainPatch.TryDeserialize(before.Serialize(), server.VertexCount * server.VertexCount, out var wire));
        client.Restore(wire);
        Assert.Equal(server.Revision, client.Revision);
        Assert.Equal(server.Heights, client.Heights);
        Assert.Equal(server.SeaLevel, client.SeaLevel);
    }
}

public sealed class AmbientSynthTests
{
    private static float Rms(Span<float> samples) { var sum = 0.0; foreach (var s in samples) sum += s * s; return (float)Math.Sqrt(sum / samples.Length); }
    private static float[] Render(AmbientMix mix, int seconds = 6, int seed = 7, Action<LifeVerse.Shared.Audio.AmbientSynth>? before = null)
    {
        var synth = new LifeVerse.Shared.Audio.AmbientSynth(22050, seed); before?.Invoke(synth);
        var buffer = new float[22050 * seconds]; synth.Fill(buffer, mix); return buffer;
    }

    [Fact]
    public void SilentMix_ProducesSilence() => Assert.True(Rms(Render(new AmbientMix(0, 0, 0))) < .0001f);

    [Theory]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0f, 1f, 0f)]
    [InlineData(0f, 0f, 1f)]
    public void EachLayer_IsAudibleAndWithinRange(float ocean, float wind, float rain)
    {
        var samples = Render(new AmbientMix(ocean, wind, rain));
        Assert.True(Rms(samples.AsSpan(22050)) > .005f, "layer is inaudible");
        Assert.All(samples, s => Assert.InRange(s, -1f, 1f));
    }

    [Fact]
    public void Master_ScalesTheOutput()
    {
        var loud = Rms(Render(new AmbientMix(1, 1, 1, 1f)).AsSpan(22050));
        var quiet = Rms(Render(new AmbientMix(1, 1, 1, .25f)).AsSpan(22050));
        var muted = Rms(Render(new AmbientMix(1, 1, 1, 0f)).AsSpan(22050));
        Assert.True(quiet < loud * .6f);
        Assert.True(muted < .0001f);
    }

    [Fact]
    public void Footstep_IsAShortBurstThatDecays()
    {
        var synth = new LifeVerse.Shared.Audio.AmbientSynth(22050, 3);
        synth.TriggerStep(1f, soft: false);
        var buffer = new float[22050]; synth.Fill(buffer, new AmbientMix(0, 0, 0));
        var start = Rms(buffer.AsSpan(0, 2000)); var tail = Rms(buffer.AsSpan(18000));
        Assert.True(start > .02f, "footstep is inaudible");
        Assert.True(tail < start * .1f, "footstep does not decay");
    }

    [Fact]
    public void SameSeed_GivesTheSameSound()
        => Assert.Equal(Render(new AmbientMix(.5f, .5f, .5f), 1, 11), Render(new AmbientMix(.5f, .5f, .5f), 1, 11));

    [Fact]
    public void Rain_AddsEnergyOverAQuietBed()
        => Assert.True(Rms(Render(new AmbientMix(.2f, .2f, 1f)).AsSpan(22050)) > Rms(Render(new AmbientMix(.2f, .2f, 0f)).AsSpan(22050)) * 1.2f);
}

public sealed class GizmoMathTests
{
    private static readonly System.Numerics.Vector3 X = System.Numerics.Vector3.UnitX;
    private static readonly System.Numerics.Vector3 Y = System.Numerics.Vector3.UnitY;
    private static readonly System.Numerics.Vector3 Z = System.Numerics.Vector3.UnitZ;

    [Fact]
    public void AxisParameter_FollowsTheMouseAlongTheAxis()
    {
        // Camera 10 m in front of the pivot looking down -Z; a ray that passes through (3, 0, 0) hits the X axis at t=3.
        var camera = new System.Numerics.Vector3(0, 0, 10);
        var through = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(3, 0, 0) - camera);
        Assert.Equal(3f, GizmoMath.AxisParameter(System.Numerics.Vector3.Zero, X, camera, through), .001f);
        var other = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(-2, 0, 0) - camera);
        Assert.Equal(-2f, GizmoMath.AxisParameter(System.Numerics.Vector3.Zero, X, camera, other), .001f);
    }

    [Fact]
    public void AxisParameter_RespectsTheAxisOrigin()
    {
        var pivot = new System.Numerics.Vector3(5, 2, -1);
        var camera = pivot + new System.Numerics.Vector3(0, 0, 8);
        var ray = System.Numerics.Vector3.Normalize(pivot + new System.Numerics.Vector3(0, 4, 0) - camera);
        Assert.Equal(4f, GizmoMath.AxisParameter(pivot, Y, camera, ray), .001f);
    }

    [Fact]
    public void AxisParameter_IsStableWhenTheRayRunsAlongTheAxis()
        => Assert.Equal(0f, GizmoMath.AxisParameter(System.Numerics.Vector3.Zero, X, new System.Numerics.Vector3(-10, 0, 0), X));

    [Fact]
    public void SignedAngle_MeasuresRotationAboutTheAxis()
    {
        var zero = System.Numerics.Vector3.Zero;
        Assert.Equal(MathF.PI / 2, GizmoMath.SignedAngle(zero, Y, Z, X), .001f);   // +Y rotates Z toward +X (right-handed)
        Assert.Equal(-MathF.PI / 2, GizmoMath.SignedAngle(zero, Y, Z, -X), .001f);
        Assert.Equal(0f, GizmoMath.SignedAngle(zero, Y, Z, Z * 4), .001f);
        Assert.Equal(0f, GizmoMath.SignedAngle(zero, Y, zero, X)); // degenerate input
    }

    [Fact]
    public void PlaneHit_FindsTheRayPlaneIntersection()
    {
        var hit = GizmoMath.PlaneHit(System.Numerics.Vector3.Zero, Y, new System.Numerics.Vector3(0, 5, 0), System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(1, -1, 0)));
        Assert.NotNull(hit);
        Assert.Equal(5f, hit!.Value.X, .001f); Assert.Equal(0f, hit.Value.Y, .001f);
        Assert.Null(GizmoMath.PlaneHit(System.Numerics.Vector3.Zero, Y, new System.Numerics.Vector3(0, 5, 0), X)); // parallel
        Assert.Null(GizmoMath.PlaneHit(System.Numerics.Vector3.Zero, Y, new System.Numerics.Vector3(0, 5, 0), Y));  // behind the camera
    }

    [Theory]
    [InlineData(1.26f, .5f, 1.5f)]
    [InlineData(-0.24f, .5f, 0f)]
    [InlineData(7.3f, 0f, 7.3f)]
    public void Snap_RoundsToTheStep(float value, float step, float expected) => Assert.Equal(expected, GizmoMath.Snap(value, step), .0001f);

    [Fact]
    public void ScaleFactor_IsTheRatioOfDistancesAndNeverCollapses()
    {
        Assert.Equal(2f, GizmoMath.ScaleFactor(1.5f, 3f));
        Assert.Equal(.05f, GizmoMath.ScaleFactor(2f, -4f));
        Assert.Equal(1f, GizmoMath.ScaleFactor(0f, 3f));
    }

    [Fact]
    public void RotateAround_KeepsTheDistanceFromTheCenter()
    {
        var center = new System.Numerics.Vector3(1, 0, 1);
        var rotated = GizmoMath.RotateAround(new System.Numerics.Vector3(3, 0, 1), center, Y, MathF.PI);
        Assert.Equal(-1f, rotated.X, .001f); Assert.Equal(1f, rotated.Z, .001f);
    }
}
