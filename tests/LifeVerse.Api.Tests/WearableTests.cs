using LifeVerse.Shared.Avatar;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Avatar 2.0, third part: what a garment or a worn piece must be to be accepted, and how custom items take part in the outfit rules.</summary>
public sealed class WearableTests
{
    private static WearableReport Check(byte[] glb) => WearableValidator.Validate(glb);
    private static string[] Codes(WearableReport report) => [.. report.Errors.Select(e => e.Code).Distinct()];
    private static byte[] Shirt(TemplateOptions? options = null, string? extras = null) => WearableTemplate.Clothing("top", WearableTemplate.ShirtBones, extras, options);
    private static byte[] Trousers(string? extras = null) => WearableTemplate.Clothing("bottom", WearableTemplate.TrouserBones, extras ?? WearableTemplate.Extras("bottom"));
    private static byte[] Hat(string? extras = null, float size = .2f) => WearableTemplate.Attachment(extras ?? WearableTemplate.Extras("hat", bone: "head", offset: [0, .15f, 0]), size);

    // ---- clothing ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(BoneNaming.Blender)] [InlineData(BoneNaming.Mixamo)] [InlineData(BoneNaming.Standard)]
    public void ARiggedShirtIsAcceptedWhateverToolNamedItsBones(BoneNaming naming)
    {
        var report = Check(Shirt(new TemplateOptions { Naming = naming }));
        Assert.True(report.Ok, string.Join("; ", report.Errors.Select(e => e.Message)));
        Assert.Equal(WearableKind.Clothing, report.Kind); Assert.Equal("top", report.Slot); Assert.Equal("base", report.Layer);
        Assert.Equal(17, report.Joints); Assert.Equal(6 * 12, report.Triangles); Assert.Equal(new[] { "spine", "chest", "upper_arm_l", "lower_arm_l", "upper_arm_r", "lower_arm_r" }.OrderBy(x => x), report.Bones.OrderBy(x => x));
        Assert.True(report.FitDeviation < .01f);
    }

    [Fact]
    public void TrousersAreForTheLegs() { var report = Check(Trousers()); Assert.True(report.Ok, string.Join("; ", report.Errors.Select(e => e.Message))); Assert.Equal("bottom", report.Slot); }

    [Fact]
    public void AGarmentMadeForAnotherPartOfTheBodyIsRefused()
    {
        var shirtAsTrousers = Check(WearableTemplate.Clothing("bottom", WearableTemplate.ShirtBones));
        Assert.False(shirtAsTrousers.Ok); Assert.Contains("wrong_region", Codes(shirtAsTrousers));
        var trousersAsShirt = Check(WearableTemplate.Clothing("top", WearableTemplate.TrouserBones, WearableTemplate.Extras("top")));
        Assert.Contains("wrong_region", Codes(trousersAsShirt));
        Assert.Contains("wrong_region", Codes(Check(WearableTemplate.Clothing("shoes", ["chest"]))));
    }

    [Fact]
    public void AGarmentWeightedToBonesOfAnotherSkeletonIsRefused()
    {
        // the standard skeleton has no "Wing" bone: weight to a bone whose name the game does not know by renaming the shoulder bone
        var renamed = Shirt();
        var from = System.Text.Encoding.ASCII.GetBytes("UpperArm.L"); var to = System.Text.Encoding.ASCII.GetBytes("WingBoneLL"); // same length keeps the file valid
        var at = renamed.AsSpan().IndexOf(from); Assert.True(at > 0); to.CopyTo(renamed, at);
        var report = Check(renamed);
        Assert.False(report.Ok, "should fail"); Assert.True(Codes(report).Contains("incompatible_bones"), string.Join(",", report.Issues.Select(i => i.Code))); Assert.Contains("WingBoneLL", report.Errors.First(e => e.Code == "incompatible_bones").Message);
    }

    [Fact]
    public void ASkeletonLaidOutDifferentlyFromOursIsRefusedOrWarned()
    {
        var stretched = Check(Shirt(new TemplateOptions { ArmStretch = 2.5f }));
        Assert.False(stretched.Ok); Assert.Contains("skeleton_mismatch", Codes(stretched)); Assert.True(stretched.FitDeviation > WearableValidator.MaxDeviation);
        var slightly = Check(Shirt(new TemplateOptions { ArmStretch = 1.35f }));
        Assert.True(slightly.Ok, string.Join("; ", slightly.Errors.Select(e => e.Message))); Assert.Contains(slightly.Warnings, w => w.Code == "loose_fit");
    }

    [Fact]
    public void AGarmentInCentimetresIsFitted()
    {
        var report = Check(Shirt(new TemplateOptions { Scale = 100 }));
        Assert.True(report.Ok, string.Join("; ", report.Errors.Select(e => e.Message)));
        Assert.Contains(report.Warnings, w => w.Code == "scaled_skeleton"); Assert.True(report.FitDeviation < .02f);
    }

    [Theory]
    [InlineData(SkinningFault.NoWeights, "bad_weights")] [InlineData(SkinningFault.BadSum, "bad_weights")] [InlineData(SkinningFault.BadIndex, "bad_joint_index")]
    public void BadSkinningIsRefused(SkinningFault fault, string code) { var report = Check(Shirt(new TemplateOptions { Fault = fault })); Assert.False(report.Ok); Assert.Contains(code, Codes(report)); }

    [Fact]
    public void TooHeavyGarmentsAreRefused() => Assert.Contains("too_many_triangles", Codes(Check(Shirt(new TemplateOptions { Copies = 300 }))));

    [Fact]
    public void ABrokenChainIsRefused() => Assert.Contains("bad_hierarchy", Codes(Check(Shirt(new TemplateOptions { BrokenArm = true }, extras: null))));

    // ---- what the file says it is ----------------------------------------------------------------------------------------------

    [Fact]
    public void WithoutExtrasTheFileIsRefusedAndTheMessageSaysWhatToAdd()
    {
        var report = Check(HumanoidTemplate.Build(new TemplateOptions { BoxBones = WearableTemplate.ShirtBones }));
        Assert.False(report.Ok); Assert.Equal("no_metadata", Codes(report)[0]); Assert.Contains("lifeverse", report.Errors.First().Message);
    }

    [Theory]
    [InlineData("{\"lifeverse\":{\"slot\":\"wings\"}}", "bad_slot")]
    [InlineData("{\"lifeverse\":{\"slot\":\"top\",\"layer\":\"middle\"}}", "bad_layer")]
    [InlineData("{\"lifeverse\":{\"slot\":\"top\",\"covers\":[\"top\"]}}", "bad_covers")]
    [InlineData("{\"lifeverse\":{\"slot\":\"top\",\"kind\":\"attachment\"}}", "kind_mismatch")]
    [InlineData("{\"lifeverse\":{\"slot\":\"hat\"}}", "slot_kind")] // a hat is a rigid piece, not rigged clothing
    public void BadDeclarationsAreRefused(string extras, string code) => Assert.Contains(code, Codes(Check(Shirt(extras: extras))));

    [Fact]
    public void ADressSaysWhatItCovers()
    {
        var report = Check(WearableTemplate.Clothing("top", ["spine", "chest", "hips", "upper_leg_l"], WearableTemplate.Extras("top", covers: ["bottom"])));
        Assert.False(report.Ok == false && Codes(report).Contains("bad_covers")); Assert.Equal(["bottom"], report.Covers);
    }

    // ---- rigid pieces -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ARigidHatIsAcceptedAndKeepsItsPlacement()
    {
        var report = Check(Hat());
        Assert.True(report.Ok, string.Join("; ", report.Errors.Select(e => e.Message)));
        Assert.Equal(WearableKind.Attachment, report.Kind); Assert.Equal("head", report.AttachBone); Assert.Equal(new float[] { 0, .15f, 0 }, report.Offset); Assert.Equal(12, report.Triangles);
    }

    [Fact]
    public void ThePieceGoesToTheDefaultBoneOfItsSlot()
    {
        Assert.Equal("head", Check(Hat(WearableTemplate.Extras("glasses"))).AttachBone);
        Assert.Equal("neck", Check(WearableTemplate.Attachment(WearableTemplate.Extras("neck"))).AttachBone);
        Assert.Equal("chest", Check(WearableTemplate.Attachment(WearableTemplate.Extras("back"))).AttachBone);
    }

    [Theory]
    [InlineData("{\"lifeverse\":{\"slot\":\"hat\",\"bone\":\"tail\"}}", "bad_bone")]
    [InlineData("{\"lifeverse\":{\"slot\":\"hat\",\"offset\":[0,9,0]}}", "bad_placement")]
    [InlineData("{\"lifeverse\":{\"slot\":\"hat\",\"scale\":50}}", "bad_placement")]
    [InlineData("{\"lifeverse\":{\"slot\":\"top\"}}", "slot_kind")] // a shirt must be rigged
    public void BadRigidPiecesAreRefused(string extras, string code) => Assert.Contains(code, Codes(Check(Hat(extras))));

    [Fact]
    public void ARigidPieceMustBeASensibleSize()
    {
        Assert.Contains("too_large", Codes(Check(Hat(size: 3f))));
        Assert.Contains("too_small", Codes(Check(Hat(size: .001f))));
        Assert.True(Check(Hat(WearableTemplate.Extras("hat", scale: .1f), size: 3f)).Ok); // a big model with a small scale is fine
    }

    // ---- files that are not glTF -------------------------------------------------------------------------------------------------

    [Fact]
    public void BrokenFilesAreExplained()
    {
        Assert.Equal("empty", Codes(Check([]))[0]);
        Assert.Equal("bad_glb", Codes(Check("not a glb........................"u8.ToArray()))[0]);
        Assert.Equal("too_big", Codes(Check(new byte[WearableValidator.MaxBytes + 1]))[0]);
        Assert.Contains("external_reference", Codes(Check(Shirt(new TemplateOptions { ExternalTexture = true }))));
    }

    [Fact]
    public void TheValidatorNeverThrowsOnRandomDamage()
    {
        var shirt = Shirt(); var hat = Hat(); var random = new Random(5);
        foreach (var original in new[] { shirt, hat })
            for (var run = 0; run < 150; run++)
            {
                var copy = (byte[])original.Clone();
                for (var hits = 0; hits < 1 + run % 5; hits++) copy[random.Next(copy.Length)] = (byte)random.Next(256);
                Assert.NotNull(Check(copy));
            }
    }

    // ---- custom items in the outfit rules ---------------------------------------------------------------------------------------

    private static readonly Guid Dress = Guid.NewGuid(), Cape = Guid.NewGuid();

    private static WearableItem? Resolver(string slot, string key)
    {
        if (key == AvatarRules.CustomKey(Dress)) return new WearableItem(slot, key, "Vestido meu", "base", ["bottom"]);
        if (key == AvatarRules.CustomKey(Cape)) return new WearableItem(slot, key, "Capa", "outer");
        return null;
    }

    [Fact]
    public void CustomKeysAreRecognized()
    {
        Assert.True(AvatarRules.IsCustom(AvatarRules.CustomKey(Dress))); Assert.False(AvatarRules.IsCustom("custom:not-a-guid")); Assert.False(AvatarRules.IsCustom("tee_basic")); Assert.False(AvatarRules.IsCustom(null));
    }

    [Fact]
    public void ACustomItemMustBeKnownToTheResolver()
    {
        var worn = new[] { ("top", AvatarRules.CustomKey(Dress)), ("bottom", "covered"), ("shoes", "sneakers_basic") };
        Assert.Contains(AvatarRules.CheckEquipment(worn), p => p.Code == "unknown_item"); // nobody told the rules about it
        Assert.Empty(AvatarRules.CheckEquipment(worn, Resolver));
        var stranger = new[] { ("top", AvatarRules.CustomKey(Guid.NewGuid())), ("bottom", "pants_basic"), ("shoes", "sneakers_basic") };
        Assert.Contains(AvatarRules.CheckEquipment(stranger, Resolver), p => p.Code == "unknown_item"); // an item that is not mine
    }

    [Fact]
    public void CustomItemsFollowTheSameConflictRules()
    {
        var conflict = new[] { ("top", AvatarRules.CustomKey(Dress)), ("bottom", "pants_basic"), ("shoes", "sneakers_basic") };
        Assert.Contains(AvatarRules.CheckEquipment(conflict, Resolver), p => p.Code == "slot_covered");
        var resolved = AvatarRules.Resolve(conflict, "top", Resolver).ToDictionary(x => x.Slot, x => x.Key);
        Assert.Equal("covered", resolved["bottom"]); Assert.Empty(AvatarRules.CheckEquipment([.. resolved.Select(x => (x.Key, x.Value))], Resolver));
        var back = AvatarRules.Resolve([.. resolved.Select(x => x.Key == "bottom" ? ("bottom", "shorts_basic") : (x.Key, x.Value))], "bottom", Resolver).ToDictionary(x => x.Slot, x => x.Key);
        Assert.Equal("shorts_basic", back["bottom"]); Assert.Equal("tee_basic", back["top"]); // choosing shorts takes the custom dress off
        Assert.Contains(AvatarRules.CheckEquipment([("jacket", AvatarRules.CustomKey(Cape)), ("top", "none"), ("bottom", "pants_basic"), ("shoes", "boots_basic")], Resolver), p => p.Code is "nothing_under" or "unknown_item");
    }

    [Fact]
    public void CustomItemsThatHideTheHairAreCounted()
    {
        WearableItem? Hider(string slot, string key) => new(slot, key, "Capuz", HidesHair: true);
        var key = AvatarRules.CustomKey(Guid.NewGuid());
        Assert.False(AvatarRules.HairVisible([("hat", key)], Hider)); Assert.True(AvatarRules.HairVisible([("hat", key)]));
    }
}
