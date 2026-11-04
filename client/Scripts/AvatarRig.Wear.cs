using Godot;
using LifeVerse.Client.Models;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// Avatar 2.0 on the procedural body: the body and face sliders (shape of every part) and the wearables of the catalog (outer layers,
/// dress and skirt, hats, glasses, jewellery, gloves, backpack). Everything is read through <see cref="AvatarRules"/>, so a value the
/// server would refuse is clamped here instead of drawn.
/// </summary>
public partial class AvatarRig
{
    /// <summary>Shapes the parts after the sliders: torso, shoulders and hips widths, arm and leg length, head size and width.</summary>
    private static void ApplyBodyShape(Node3D hips, Node3D torso, Node3D head, IReadOnlyDictionary<string, float> body, IReadOnlyDictionary<string, float> face)
    {
        hips.Scale = new Vector3(body["hips"], 1, body["hips"]);
        torso.Scale = new Vector3(body["torso"] / Mathf.Max(body["hips"], .01f), 1, body["torso"] / Mathf.Max(body["hips"], .01f));
        foreach (var arm in new[] { "LeftUpperArm", "RightUpperArm" }) if (torso.GetNodeOrNull<Node3D>(arm) is { } node) node.Scale = new Vector3(1, body["arms"], 1);
        foreach (var leg in new[] { "LeftUpperLeg", "RightUpperLeg" }) if (hips.GetNodeOrNull<Node3D>(leg) is { } node) node.Scale = new Vector3(1, body["legs"], 1);
        head.Scale = new Vector3(body["head"] * face["headWidth"], body["head"], body["head"]);
    }

    private static Material Tint(string html) => new StandardMaterial3D { AlbedoColor = Color.FromHtml(html), Roughness = .62f };

    /// <summary>Draws what is worn on top of the body. Every piece is a child of the part it follows, so it moves with the animation.</summary>
    private static void AddWearables(Node3D hips, Node3D torso, Node3D head, AvatarAppearanceData data, IReadOnlyDictionary<string, float> face)
    {
        string Item(string slot) => AvatarRules.IsCustom(data.Item(slot, "none")) ? "none" : data.Item(slot, "none"); // custom items are drawn from their own file
        string TintOf(string slot, string fallback) => data.Tint(slot, fallback);

        // outer layer over the top: a coat reaches the knees and covers the arms, a vest only the chest
        switch (Item("jacket"))
        {
            case "coat_long":
                {
                    var material = Tint(TintOf("jacket", "#5B4636"));
                    Part(torso, "Coat", new Vector3(0, -.05f, 0), new Vector3(.68f, .86f, .38f), material);
                    Part(hips, "CoatSkirt", new Vector3(0, -.32f, 0), new Vector3(.56f, .52f, .34f), material);
                    foreach (var arm in new[] { "LeftUpperArm", "RightUpperArm" }) if (torso.GetNodeOrNull<Node3D>(arm) is { } node) Part(node, "CoatSleeve", new Vector3(0, -.02f, 0), new Vector3(.25f, .52f, .25f), material);
                    break;
                }
            case "vest_basic": Part(torso, "Vest", new Vector3(0, .02f, 0), new Vector3(.66f, .6f, .36f), Tint(TintOf("jacket", "#2E4057"))); break;
        }

        // a dress covers the legs down to the knees; a skirt does the same for the bottom only
        if (data.Item("top", "tee_basic") == "dress_basic") Part(hips, "Dress", new Vector3(0, -.34f, 0), new Vector3(.64f, .78f, .4f), Tint(TintOf("top", "#8E3B6B")));
        if (data.Item("bottom", "pants_basic") == "skirt_basic") Part(hips, "Skirt", new Vector3(0, -.3f, 0), new Vector3(.58f, .46f, .38f), Tint(TintOf("bottom", "#6B4A8E")));

        // head: hats, glasses and the things that hang from the ears
        switch (Item("hat"))
        {
            case "cap_basic":
                Part(head, "Cap", new Vector3(0, .34f, 0), new Vector3(.56f, .14f, .56f), Tint(TintOf("hat", "#B23A48")));
                Part(head, "CapBrim", new Vector3(0, .28f, -.34f), new Vector3(.4f, .04f, .22f), Tint(TintOf("hat", "#B23A48")));
                break;
            case "hat_basic": Part(head, "Hat", new Vector3(0, .38f, 0), new Vector3(.56f, .12f, .56f), Tint(TintOf("hat", "#E9C46A"))); break;
            case "beanie_basic": Part(head, "Beanie", new Vector3(0, .3f, 0), new Vector3(.6f, .3f, .58f), Tint(TintOf("hat", "#3D5A80"))); break;
        }
        var spacing = face["eyeSpacing"];
        if (Item("glasses") is { } glasses and not "none")
        {
            var frame = Tint(TintOf("glasses", "#222222")); var size = glasses == "glasses_square" ? new Vector3(.2f, .14f, .03f) : new Vector3(.2f, .12f, .03f);
            Part(head, "GlassesLeft", new Vector3(-.14f * spacing, .05f, -.27f), size, frame);
            Part(head, "GlassesRight", new Vector3(.14f * spacing, .05f, -.27f), size, frame);
            Part(head, "GlassesBridge", new Vector3(0, .06f, -.27f), new Vector3(.08f, .02f, .03f), frame);
        }
        if (Item("ears") == "earrings_hoop")
        {
            var gold = Tint(TintOf("ears", "#E9C46A"));
            Sphere(head, "EarringLeft", new Vector3(-.28f, -.06f, 0), new Vector3(.03f, .05f, .03f), gold);
            Sphere(head, "EarringRight", new Vector3(.28f, -.06f, 0), new Vector3(.03f, .05f, .03f), gold);
        }

        // neck and chest
        switch (Item("neck"))
        {
            case "necklace_chain": Part(torso, "Necklace", new Vector3(0, .36f, -.17f), new Vector3(.2f, .14f, .02f), Tint(TintOf("neck", "#E9C46A"))); break;
            case "scarf_basic": Part(torso, "Scarf", new Vector3(0, .44f, 0), new Vector3(.3f, .12f, .26f), Tint(TintOf("neck", "#C0392B"))); break;
        }

        // gloves tint the hands; a backpack rides on the back
        if (Item("gloves") == "gloves_basic")
            foreach (var arm in new[] { "LeftUpperArm/LeftLowerArm/LeftHand", "RightUpperArm/RightLowerArm/RightHand" })
                if (torso.GetNodeOrNull<Node3D>(arm) is { } hand) Sphere(hand, "Glove", Vector3.Zero, new Vector3(.14f, .17f, .12f), Tint(TintOf("gloves", "#222222")));
        if (Item("back") == "backpack_basic") Part(torso, "Backpack", new Vector3(0, .05f, .27f), new Vector3(.42f, .5f, .2f), Tint(TintOf("back", "#7A5C3E")));
    }

    /// <summary>The features of the face that follow the face sliders: nose, mouth, jaw, brow and the spacing of the eyes.</summary>
    private static void AddFaceFeatures(Node3D head, IReadOnlyDictionary<string, float> face, string skinColor)
    {
        var skin = Tint(skinColor);
        Part(head, "Nose", new Vector3(0, -.02f, -.27f), new Vector3(.06f, .1f, .08f) * face["noseSize"], skin);
        Part(head, "Mouth", new Vector3(0, -.13f, -.26f), new Vector3(.14f * face["mouthWidth"], .025f, .02f), Tint("#7A3B3B"));
        Part(head, "Jaw", new Vector3(0, -.2f, -.04f), new Vector3(.34f * face["jawWidth"], .1f, .3f), skin);
        var brow = Tint("#3A2A20"); var y = .12f * face["browHeight"];
        Part(head, "BrowLeft", new Vector3(-.105f * face["eyeSpacing"], y, -.262f), new Vector3(.1f, .018f, .02f), brow);
        Part(head, "BrowRight", new Vector3(.105f * face["eyeSpacing"], y, -.262f), new Vector3(.1f, .018f, .02f), brow);
    }
}
