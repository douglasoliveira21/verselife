using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Worlds;
using LifeVerse.Shared.Building;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class ObjectResourcesTests
{
    private static float Settle(FlexSettings flex, float seconds = 6f)
    {
        var sim = new FlexSimulation(); for (var t = 0f; t < seconds; t += .016f) sim.Step(flex, .016f, t, 0);
        return sim.Lean.Length();
    }

    [Fact]
    public void ARigidObjectNeverMovesAndAForceLeansASoftOneTowardsIt()
    {
        Assert.Equal(0f, Settle(new FlexSettings(0, 0, 2, 5, 1, 5, 0, 0)), 3);
        var sim = new FlexSimulation(); for (var t = 0f; t < 8f; t += .016f) sim.Step(new FlexSettings(2, 0, 3, 0, 1, 5, 0, 0), .016f, t, 0);
        Assert.True(sim.Lean.X > .3f && Math.Abs(sim.Lean.Y) < .01f, $"leans along +X: {sim.Lean}");
    }

    [Fact]
    public void TensionHoldsItBackAndDragSettlesIt()
    {
        Assert.True(Settle(new FlexSettings(2, 0, 3, 0, 0, 5, 0, 0)) > Settle(new FlexSettings(2, 0, 3, 0, 10, 5, 0, 0)));
        var windy = new FlexSimulation(); var calm = 0f; for (var t = 0f; t < 6f; t += .016f) windy.Step(new FlexSettings(2, 0, 3, 8, 1, 0, 0, 0), .016f, t, 0);
        Assert.True(windy.Lean.Length() > .01f); // the wind moves it
        Assert.Equal(0f, calm);
        var sagging = new FlexSimulation(); sagging.Step(new FlexSettings(3, 10, 2, 0, 1, 0, 0, 0), .016f, 0, 0); Assert.True(sagging.Sag < 0);
    }

    [Fact]
    public void ExtremeSettingsStayInRangeAndFromMaterialReadsWhatWasWritten()
    {
        Assert.True(Settle(new FlexSettings(99, 99, -5, 99, -5, 99, 99, 99)) <= 1.0001f);
        var json = "{\"flex\":" + new FlexSettings(2, 1, 3, 4, 5, 6, 7, 8).ToJson().ToJsonString() + "}";
        Assert.Equal(new FlexSettings(2, 1, 3, 4, 5, 6, 7, 8), FlexSettings.FromMaterial(json)); Assert.Null(FlexSettings.FromMaterial("{}"));
        Assert.Equal(10f, FlexSettings.FromMaterial("{\"flex\":{\"wind\":500}}")!.Value.Wind);
    }

    [Fact]
    public void TheServerChecksFlexLightAndProjectorSettings()
    {
        Assert.Null(MaterialAssets.ValidateSettings("{\"light\":{\"energy\":2,\"range\":8,\"falloff\":1.5},\"projectorBuiltin\":\"bricks\",\"projector\":{\"fov\":60,\"focus\":10,\"ambiance\":0.2},\"flex\":{\"softness\":2,\"wind\":4,\"forceX\":-3}}"));
        Assert.NotNull(MaterialAssets.ValidateSettings("{\"flex\":{\"softness\":9}}"));
        Assert.NotNull(MaterialAssets.ValidateSettings("{\"flex\":{\"forceZ\":-11}}"));
        Assert.NotNull(MaterialAssets.ValidateSettings("{\"light\":{\"energy\":1,\"range\":5,\"falloff\":9}}"));
        Assert.NotNull(MaterialAssets.ValidateSettings("{\"projectorBuiltin\":\"bricks\"}")); // a projector needs the light
        Assert.NotNull(MaterialAssets.ValidateSettings("{\"light\":{\"energy\":1,\"range\":5},\"projectorBuiltin\":\"bricks\",\"projector\":{\"fov\":5}}"));
        Assert.NotNull(MaterialAssets.ValidateSettings("{\"light\":{\"energy\":1,\"range\":5},\"projectorBuiltin\":\"nope\",\"projector\":{}}"));
        var id = Guid.NewGuid(); Assert.Contains(id, MaterialAssets.TextureIds("{\"light\":{},\"projectorTexture\":\"" + id + "\"}")); // the projector image is one of the parts the object is made of
        Assert.True(BuildCost.Of("cube", "{}", "{\"flex\":{\"softness\":1}}", "{}", "[]") > BuildCost.Of("cube", "{}", "{}", "{}", "[]"));
    }

    [Fact]
    public void TheServerChecksTheAdvancedMaterialSettingsOfTheObjectAndOfEachFace()
    {
        Assert.Null(MaterialAssets.ValidateSettings("{\"fullBright\":true,\"doubleSided\":true,\"alphaMode\":\"mask\",\"alphaCutoff\":0.4,\"mapping\":\"planar\",\"faces\":[{\"index\":2,\"alphaMode\":\"blend\",\"fullBright\":false}]}"));
        Assert.NotNull(MaterialAssets.ValidateSettings("{\"alphaMode\":\"glow\"}"));
        Assert.NotNull(MaterialAssets.ValidateSettings("{\"alphaCutoff\":2}"));
        Assert.NotNull(MaterialAssets.ValidateSettings("{\"mapping\":\"cubic\"}"));
        Assert.NotNull(MaterialAssets.ValidateSettings("{\"fullBright\":\"sim\"}"));
        Assert.NotNull(MaterialAssets.ValidateSettings("{\"faces\":[{\"index\":1,\"doubleSided\":1}]}"));
    }
}
