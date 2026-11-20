using System.Collections.Generic;
using LifeVerse.Shared.Networking;
using LifeVerse.Unity.Avatar;
using NUnit.Framework;
using UnityEngine;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class FingerTests
    {
        /// <summary>An arm out to the right with the palm down and five fingers of three bones each, in the way a body is made (T-pose).</summary>
        private static (Transform hand, List<Transform> all, GameObject root) Arm(string style)
        {
            var root = new GameObject("arm"); var hand = new GameObject("hand").transform; hand.SetParent(root.transform, false); hand.position = new Vector3(1f, 1.4f, 0f); var all = new List<Transform> { hand };
            var names = new[] { "thumb", "index", "middle", "ring", "pinky" }; var spread = new[] { -.05f, .035f, .012f, -.012f, -.035f };   // z positions across the hand
            for (var f = 0; f < 5; f++)
            {
                Transform parent = hand;
                for (var s = 1; s <= 3; s++)
                {
                    var name = style == "mixamo" ? "RightHand" + char.ToUpperInvariant(names[f][0]) + names[f].Substring(1) + s : names[f] + "_0" + s + "_r"; var bone = new GameObject(name).transform; bone.SetParent(parent, false);
                    bone.position = new Vector3(1.08f + (s - 1) * .035f, 1.4f, spread[f]); parent = bone; all.Add(bone);
                }
            }
            return (hand, all, root);
        }

        [Test]
        public void FingerBonesAreFoundByNameInBothStyles()
        {
            Assert.AreEqual(('r', Finger.Index, 2), HandFingers.Parse("index_02_r").Value); Assert.AreEqual(('l', Finger.Pinky, 3), HandFingers.Parse("LeftHandPinky3").Value); Assert.AreEqual(('r', Finger.Thumb, 1), HandFingers.Parse("mixamorig:RightHandThumb1").Value);
            Assert.IsNull(HandFingers.Parse("upperarm_r")); Assert.IsNull(HandFingers.Parse("index_04_r"));
        }

        [TestCase("unreal")] [TestCase("mixamo")]
        public void FingersCurlTowardThePalmAndPointingKeepsOneOut(string style)
        {
            var (hand, all, root) = Arm(style); var fingers = new HandFingers(); fingers.Prepare(all, side => side == 'r' ? hand : null);
            Assert.IsTrue(fingers.HasFingers); Assert.AreEqual(15, fingers.BoneCount); Assert.AreEqual(0f, fingers.BendOf('r', Finger.Middle), 1f, "straight at rest"); fingers.Apply(side => HandShape.Relaxed, 1f / 60f); fingers.Apply(side => HandShape.Relaxed, 1f / 60f); var twice = fingers.BendOf('r', Finger.Ring); for (var i = 0; i < 30; i++) fingers.Apply(side => HandShape.Relaxed, 1f / 60f); Assert.AreEqual(twice, fingers.BendOf('r', Finger.Ring), .5f, "the same shape every frame does not keep curling more");
            for (var i = 0; i < 120; i++) fingers.Apply(side => HandShape.Pointing, 1f / 60f);
            Assert.Greater(fingers.BendOf('r', Finger.Middle), 60f, "the middle finger closes"); Assert.Greater(fingers.BendOf('r', Finger.Pinky), 60f); Assert.Less(fingers.BendOf('r', Finger.Index), 12f, "the index stays out to point");
            Assert.Greater(fingers.CurlOf('r', Finger.Middle), .9f);
            for (var i = 0; i < 240; i++) fingers.Apply(side => HandShape.Open, 1f / 60f); Assert.Less(fingers.BendOf('r', Finger.Middle), 20f, "an open hand straightens them again"); Assert.AreEqual(0, fingers.CurlOf('l', Finger.Index));
            Object.DestroyImmediate(root);
        }

        [Test]
        public void CurlingBringsTheTipsTowardTheSideThePalmFaces()
        {
            var (hand, all, root) = Arm("unreal"); var fingers = new HandFingers(); fingers.Prepare(all, side => side == 'r' ? hand : null); var tip = all.Find(t => t.name == "middle_03_r"); var before = tip.position.y;
            for (var i = 0; i < 120; i++) fingers.Apply(side => HandShape.Grip, 1f / 60f);
            Assert.Less(tip.position.y, before - .01f, "the palm is down in this pose, so the fingers curl down");
            Object.DestroyImmediate(root);
        }

        [Test]
        public void TheShapeOfTheHandFollowsWhatThePersonDoes()
        {
            Assert.AreEqual(HandShape.Pointing.Index, HandShape.For(NetworkMotionState.Idle, AvatarView.Gesture.Point, 'r', false).Index); Assert.AreEqual(HandShape.Relaxed.Index, HandShape.For(NetworkMotionState.Idle, AvatarView.Gesture.Point, 'l', false).Index);
            Assert.AreEqual(HandShape.Grip.Index, HandShape.For(NetworkMotionState.Idle, AvatarView.Gesture.None, 'r', true).Index); Assert.AreEqual(HandShape.Open.Index, HandShape.For(NetworkMotionState.Wave, AvatarView.Gesture.None, 'r', false).Index);
            Assert.AreEqual(HandShape.Typing.Index, HandShape.For(NetworkMotionState.Typing, AvatarView.Gesture.None, 'l', false).Index); Assert.AreEqual(HandShape.Loose.Index, HandShape.For(NetworkMotionState.Run, AvatarView.Gesture.None, 'l', false).Index);
        }
    }
}
