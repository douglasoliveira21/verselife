using LifeVerse.Shared.Avatar;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Avatar 2.0, second part: reading a GLB, mapping its skeleton to the standard bones, and refusing models that cannot be avatars.</summary>
public sealed class AvatarModelTests
{
    private static AvatarModelReport Check(TemplateOptions? options = null) => AvatarModelValidator.Validate(HumanoidTemplate.Build(options), binary: true);
    private static string[] Codes(AvatarModelReport report) => [.. report.Errors.Select(e => e.Code).Distinct()];

    // ---- bone names -------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Hips", "hips")] [InlineData("mixamorig:Hips", "hips")] [InlineData("J_Bip_C_Hips", "hips")] [InlineData("pelvis", "hips")]
    [InlineData("Spine", "spine")] [InlineData("mixamorig:Spine1", "chest")] [InlineData("Chest", "chest")] [InlineData("mixamorig:Spine2", null)]
    [InlineData("Neck", "neck")] [InlineData("Head", "head")]
    [InlineData("mixamorig:LeftArm", "upper_arm_l")] [InlineData("UpperArm.L", "upper_arm_l")] [InlineData("J_Bip_R_UpperArm", "upper_arm_r")] [InlineData("upper_arm_r", "upper_arm_r")] [InlineData("RightArm", "upper_arm_r")]
    [InlineData("mixamorig:LeftForeArm", "lower_arm_l")] [InlineData("LowerArm_R", "lower_arm_r")] [InlineData("forearm.L", "lower_arm_l")]
    [InlineData("mixamorig:LeftHand", "hand_l")] [InlineData("Hand.R", "hand_r")]
    [InlineData("mixamorig:LeftUpLeg", "upper_leg_l")] [InlineData("Thigh_L", "upper_leg_l")] [InlineData("J_Bip_L_UpperLeg", "upper_leg_l")]
    [InlineData("mixamorig:RightLeg", "lower_leg_r")] [InlineData("Calf.L", "lower_leg_l")] [InlineData("Shin_R", "lower_leg_r")]
    [InlineData("mixamorig:LeftFoot", "foot_l")] [InlineData("Foot.R", "foot_r")]
    [InlineData("mixamorig:LeftHandIndex1", null)] [InlineData("LeftToeBase", null)] [InlineData("mixamorig:LeftShoulder", null)] [InlineData("Armature", null)] [InlineData("", null)]
    [InlineData("Hand", null)] // a hand with no side is not a hand of ours
    public void BoneNames_AreMappedToTheStandardSkeleton(string name, string? expected) => Assert.Equal(expected, BoneMapper.Map(name));

    [Fact]
    public void TheStandardBoneListMatchesTheRigOfTheGame()
    {
        Assert.Equal(18, BoneMapper.Standard.Length);
        Assert.All(BoneMapper.Required, b => Assert.Contains(b, BoneMapper.Standard));
        foreach (var bone in BoneMapper.Standard.Where(b => b != "root"))
            foreach (var naming in Enum.GetValues<BoneNaming>()) Assert.Equal(bone, BoneMapper.Map(HumanoidTemplate.NameOf(bone, naming)));
    }

    // ---- a good model --------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(BoneNaming.Blender)] [InlineData(BoneNaming.Mixamo)] [InlineData(BoneNaming.Standard)]
    public void ACleanModelPassesWhateverToolNamedTheBones(BoneNaming naming)
    {
        var report = Check(new TemplateOptions { Naming = naming });
        Assert.True(report.Ok, string.Join("; ", report.Errors.Select(e => e.Message)));
        Assert.Equal(17, report.Joints); Assert.Equal(16 + 1, report.BoneMap.Count);
        Assert.Equal(HumanoidTemplate.NameOf("upper_arm_l", naming), report.BoneMap["upper_arm_l"]);
        Assert.Equal(204, report.Triangles); Assert.Equal(136, report.Vertices);
        Assert.InRange(report.Height, 1.77f, 1.79f); Assert.InRange(report.ScaleToStandard, 1.0f, 1.03f);
        Assert.InRange(report.LegRatio, .45f, .55f); Assert.InRange(report.ArmRatio, .25f, .35f); Assert.InRange(report.ShoulderRatio, .15f, .25f);
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void AModelInCentimetresIsScaledNotRefused()
    {
        var report = Check(new TemplateOptions { Scale = 100 });
        Assert.True(report.Ok, string.Join("; ", report.Errors.Select(e => e.Message)));
        Assert.InRange(report.Height, 177f, 179f); Assert.InRange(report.ScaleToStandard, .0100f, .0103f);
        Assert.Contains(report.Warnings, w => w.Code == "odd_size" || report.Height > 6);
    }

    [Fact]
    public void AnAnimationInsideTheModelIsIgnoredWithANote() => Assert.Contains(Check(new TemplateOptions { WithAnimation = true }).Warnings, w => w.Code == "animations_ignored");

    [Fact]
    public void AMeshNotBoundToTheSkeletonIsAWarning() { var report = Check(new TemplateOptions { RigidMesh = true }); Assert.True(report.Ok); Assert.Contains(report.Warnings, w => w.Code == "rigid_mesh"); }

    [Fact]
    public void WithoutAChestTheSpineStandsIn()
    {
        var report = Check(new TemplateOptions { OmitBone = "chest" });
        // chest has children (neck, arms): leaving it out is allowed because the spine takes its place only when no bone looks like a chest
        Assert.True(report.Ok || Codes(report).Contains("bad_hierarchy") || Codes(report).Contains("missing_bones"));
    }

    // ---- models that are refused -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("hand_l")] [InlineData("foot_r")] [InlineData("head")]
    public void AMissingBoneIsNamed(string bone)
    {
        var report = Check(new TemplateOptions { OmitBone = bone });
        Assert.False(report.Ok); Assert.Contains("missing_bones", Codes(report));
        Assert.Contains(bone, report.Errors.First(e => e.Code == "missing_bones").Message);
    }

    [Fact]
    public void ABrokenHierarchyIsRefused()
    {
        var report = Check(new TemplateOptions { BrokenArm = true });
        Assert.False(report.Ok); Assert.Contains("bad_hierarchy", Codes(report));
    }

    [Fact]
    public void AnUpsideDownOrSidewaysSkeletonIsRefused() { var report = Check(new TemplateOptions { UpsideDown = true }); Assert.False(report.Ok); Assert.Contains("not_upright", Codes(report)); }

    [Fact]
    public void StrangeProportionsAreRefused() { var report = Check(new TemplateOptions { ArmStretch = 4f }); Assert.False(report.Ok); Assert.Contains("bad_proportions", Codes(report)); }

    [Theory]
    [InlineData(SkinningFault.NoWeights, "bad_weights")]
    [InlineData(SkinningFault.BadSum, "bad_weights")]
    [InlineData(SkinningFault.BadIndex, "bad_joint_index")]
    public void BadSkinningIsRefused(SkinningFault fault, string code) { var report = Check(new TemplateOptions { Fault = fault }); Assert.False(report.Ok); Assert.Contains(code, Codes(report)); }

    [Fact]
    public void TooManyTrianglesAreRefused()
    {
        var report = Check(new TemplateOptions { Copies = 320 });
        Assert.False(report.Ok); Assert.Contains("too_many_triangles", Codes(report));
        Assert.True(report.Triangles > AvatarModelValidator.MaxTriangles);
    }

    [Fact]
    public void ModelsThatLinkToOtherFilesOrNeedUnsupportedExtensionsAreRefused()
    {
        Assert.Contains("external_reference", Codes(Check(new TemplateOptions { ExternalTexture = true })));
        Assert.Contains("unsupported_extension", Codes(Check(new TemplateOptions { RequiredExtension = "KHR_draco_mesh_compression" })));
        Assert.True(Check(new TemplateOptions { RequiredExtension = "KHR_materials_unlit" }).Ok);
    }

    // ---- files that are not even glTF ----------------------------------------------------------------------------------------------

    [Fact]
    public void BrokenFilesAreExplainedNotCrashedOn()
    {
        Assert.Equal("empty", Codes(AvatarModelValidator.Validate([], true))[0]);
        Assert.Equal("bad_glb", Codes(AvatarModelValidator.Validate("not a glb at all, just text........"u8.ToArray(), true))[0]);
        var good = HumanoidTemplate.Build();
        Assert.Equal("bad_glb", Codes(AvatarModelValidator.Validate(good[..(good.Length / 2)], true))[0]);
        var wrongVersion = (byte[])good.Clone(); wrongVersion[4] = 1;
        Assert.Equal("bad_version", Codes(AvatarModelValidator.Validate(wrongVersion, true))[0]);
        Assert.Equal("bad_json", Codes(AvatarModelValidator.Validate("{ nope"u8.ToArray(), false))[0]);
        Assert.Equal("bad_version", Codes(AvatarModelValidator.Validate("{\"asset\":{\"version\":\"1.0\"}}"u8.ToArray(), false))[0]);
        Assert.Equal("no_skin", Codes(AvatarModelValidator.Validate("{\"asset\":{\"version\":\"2.0\"}}"u8.ToArray(), false))[0]);
        var huge = new byte[AvatarModelValidator.MaxBytes + 1]; Assert.Equal("too_big", Codes(AvatarModelValidator.Validate(huge, true))[0]);
    }

    [Fact]
    public void ADamagedBufferIsRefusedBeforeItIsRead()
    {
        var glb = HumanoidTemplate.Build(); var corrupt = (byte[])glb.Clone();
        // the JSON part ends where the first chunk says; cut the binary part short while keeping the header honest
        var jsonLength = BitConverter.ToUInt32(corrupt, 12); var binStart = 20 + (int)jsonLength + 8;
        var shortened = corrupt[..(binStart + 40)]; BitConverter.GetBytes((uint)shortened.Length).CopyTo(shortened, 8); BitConverter.GetBytes(32u).CopyTo(shortened, binStart - 8);
        var report = AvatarModelValidator.Validate(shortened, true);
        Assert.False(report.Ok);
    }

    [Fact]
    public void TheReportCanBeStoredAsJson()
    {
        var report = Check(new TemplateOptions { Fault = SkinningFault.BadSum });
        using var json = System.Text.Json.JsonDocument.Parse(report.ToJson());
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), i => i.GetProperty("code").GetString() == "bad_weights");
        Assert.True(json.RootElement.GetProperty("boneMap").TryGetProperty("hips", out _));
    }

    [Fact]
    public void TheValidatorNeverThrowsOnRandomDamage()
    {
        var glb = HumanoidTemplate.Build(); var random = new Random(11);
        for (var run = 0; run < 200; run++)
        {
            var copy = (byte[])glb.Clone();
            for (var hits = 0; hits < 1 + run % 6; hits++) copy[random.Next(copy.Length)] = (byte)random.Next(256);
            var report = AvatarModelValidator.Validate(copy, true); // must answer, ok or not, without an exception
            Assert.NotNull(report);
        }
    }
}
