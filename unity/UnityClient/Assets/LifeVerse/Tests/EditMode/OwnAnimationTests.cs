using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Networking;
using LifeVerse.Unity.Avatar;
using LifeVerse.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LifeVerse.Unity.Tests.EditMode
{
    /// <summary>The person's own animations play in every state the avatar window offers, and the whole inventory can be worn.</summary>
    public sealed class OwnAnimationTests
    {
        private static readonly NetworkMotionState[] Motions = (NetworkMotionState[])System.Enum.GetValues(typeof(NetworkMotionState));
        private static readonly AvatarView.Gesture[] Gestures = (AvatarView.Gesture[])System.Enum.GetValues(typeof(AvatarView.Gesture));

        [Test]
        public void EveryStateTheWindowOffersCanHappen()
        {
            var reached = new HashSet<string>(from m in Motions from g in Gestures from speed in new[] { 0f, 3f } let state = SkeletalBody.ActivityOf(m, g, speed) where state != null select state) { "Landing", "StandUp" };   // the two moments between states
            CollectionAssert.IsSubsetOf(AnimationStates.All.Keys.ToList(), reached.ToList());
            CollectionAssert.IsSubsetOf(reached.ToList(), AnimationStates.All.Keys.ToList(), "nothing asks for a state that does not exist");
            Assert.AreEqual("Greet", SkeletalBody.ActivityOf(NetworkMotionState.Idle, AvatarView.Gesture.Greet, 0f));
            Assert.AreEqual("Sit", SkeletalBody.ActivityOf(NetworkMotionState.Sit, AvatarView.Gesture.Greet, 0f), "seated, a gesture does not stand the person up");
            Assert.IsNull(SkeletalBody.ActivityOf(NetworkMotionState.Typing, AvatarView.Gesture.None, 0f)); Assert.IsNull(SkeletalBody.ActivityOf(NetworkMotionState.Swim, AvatarView.Gesture.None, 0f));
        }

        private static IEnumerator Done(Task task)
        {
            var until = System.DateTime.UtcNow.AddSeconds(30); while (!task.IsCompleted) { Assert.Less(System.DateTime.UtcNow, until, "the model did not load in time"); yield return null; }
            if (task.IsFaulted) throw task.Exception.GetBaseException();
        }

        [UnityTest]
        public IEnumerator OnesOwnAnimationPlaysSeatedGesturingLandingAndGettingUp()
        {
            var parent = new GameObject("own-animations");
            try
            {
                var import = new GLTFast.GltfImport(deferAgent: new GLTFast.UninterruptedDeferAgent()); var load = import.LoadGltfBinary(HumanoidTemplate.Build()); yield return Done(load);
                var creating = SkeletalBody.CreateAsync(parent.transform, import); yield return Done(creating); var body = creating.Result; Assert.IsNotNull(body);
                var bytes = Resources.Load<TextAsset>("Animations/default_avatar_animations").bytes; var name = SkeletonClip.Names(bytes)[0];
                foreach (var state in AnimationStates.All.Keys) body.Overrides[state] = SkeletonClip.Read(bytes, name);   // a clip of one's own for every state
                var length = body.Overrides["Idle"].Duration; Assert.Greater(length, .1f);
                string After(NetworkMotionState motion, AvatarView.Gesture gesture, float seconds = .05f) { body.Tick(seconds, motion, gesture, 0f, 0f, new PoseValues(), 0f, true); return body.OwnState; }
                Assert.AreEqual("Idle", After(NetworkMotionState.Idle, AvatarView.Gesture.None)); Assert.IsTrue(body.UsingClip);
                foreach (var (motion, state) in new[] { (NetworkMotionState.Wave, "Wave"), (NetworkMotionState.Dance, "Dance"), (NetworkMotionState.Jump, "Jump"), (NetworkMotionState.Sit, "Sit") }) Assert.AreEqual(state, After(motion, AvatarView.Gesture.None));
                Assert.AreEqual("StandUp", After(NetworkMotionState.Idle, AvatarView.Gesture.None), "getting up from the seat");
                Assert.AreEqual("StandUp", After(NetworkMotionState.Idle, AvatarView.Gesture.None, length / 2f));
                Assert.AreEqual("Idle", After(NetworkMotionState.Idle, AvatarView.Gesture.None, length), "once it has played, standing again");
                foreach (var gesture in Gestures.Where(g => g != AvatarView.Gesture.None)) Assert.AreEqual(gesture.ToString(), After(NetworkMotionState.Idle, gesture));
                Assert.AreEqual("StandUp", After(NetworkMotionState.Idle, AvatarView.Gesture.None), "after sitting on the floor");
                After(NetworkMotionState.Fall, AvatarView.Gesture.None); Assert.AreEqual("Landing", After(NetworkMotionState.Idle, AvatarView.Gesture.None));
                Assert.AreEqual("Walk", After(NetworkMotionState.Walk, AvatarView.Gesture.None), "walking cuts the landing short");
                Assert.AreEqual("Idle", After(NetworkMotionState.Idle, AvatarView.Gesture.None));
                body.Overrides.Remove("Dance"); Assert.AreEqual("", After(NetworkMotionState.Dance, AvatarView.Gesture.None), "with none of one's own, the game's"); Assert.IsFalse(body.UsingClip, "dancing is drawn by the game's own pose");
                body.Overrides.Remove("Landing"); After(NetworkMotionState.Fall, AvatarView.Gesture.None); Assert.AreEqual("Idle", After(NetworkMotionState.Idle, AvatarView.Gesture.None));
            }
            finally { Object.DestroyImmediate(parent); }
        }

        [Test]
        public void APageOfTheInventorySaysWhetherThereIsMore()
        {
            Assert.IsTrue(AttachmentsWindow.HasMore("{\"page\":1,\"hasMore\":true,\"items\":[]}")); Assert.IsFalse(AttachmentsWindow.HasMore("{\"hasMore\":false,\"items\":[]}"));
            Assert.IsFalse(AttachmentsWindow.HasMore("{\"items\":[]}")); Assert.IsFalse(AttachmentsWindow.HasMore("[]")); Assert.IsFalse(AttachmentsWindow.HasMore("nada"));
            Assert.AreEqual(100, AttachmentsWindow.ItemsPerPage, "what the platform allows at most");
        }
    }
}
