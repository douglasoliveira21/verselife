using LifeVerse.Shared.Networking;
using LifeVerse.Unity.Avatar;
using LifeVerse.Unity.World;
using NUnit.Framework;
using UnityEngine;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class AvatarTests
    {
        private GameObject _root;
        [TearDown] public void Clean() { if (_root != null) Object.DestroyImmediate(_root); }

        private AvatarView Make() { var view = AvatarView.Create(1, "teste", Color.red); _root = view.gameObject; return view; }

        [Test]
        public void ABodyStandsWhereTheGameSaysWithTheZAxisFlipped()
        {
            var view = Make(); view.PlaceAt(new Vector3(3f, 2f, -5f));
            Assert.AreEqual(0f, Vector3.Distance(view.transform.position, GameSpace.ToUnity(3f, 2f, -5f)), 1e-5f);
        }

        [Test]
        public void TheBodyFacesWhereTheServerYawSays()
        {
            var view = Make();
            // yaw 0 is walking toward -z of the game (+z of Unity); yaw +90 degrees is toward +x of the game
            view.ApplySnapshot(new PlayerSnapshot(1, 0, 0, 0, 0, 0, -3f, 0f, NetworkMotionState.Walk, 0)); view.Tick(1f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, view.transform.eulerAngles.y), .5f);
            view.ApplySnapshot(new PlayerSnapshot(1, 0, 0, 0, 3f, 0, 0, Mathf.PI / 2f, NetworkMotionState.Walk, 0)); for (var i = 0; i < 60; i++) view.Tick(.05f);
            var forward = view.transform.forward; Assert.AreEqual(1f, forward.x, .05f); Assert.AreEqual(0f, forward.z, .05f);
        }

        [Test]
        public void WalkingSwingsTheLegsAndStandingStillDoesNot()
        {
            var view = Make(); view.ApplySnapshot(new PlayerSnapshot(1, 0, 0, 0, 0, 0, -3.2f, 0f, NetworkMotionState.Walk, 0));
            var best = 0f; for (var i = 0; i < 60; i++) { view.Tick(.03f); best = Mathf.Max(best, Mathf.Abs(Mathf.DeltaAngle(0f, view.LeftLeg.localEulerAngles.x))); }
            Assert.Greater(best, 10f);
            view.ApplySnapshot(new PlayerSnapshot(1, 0, 0, 0, 0, 0, 0, 0f, NetworkMotionState.Idle, 0)); for (var i = 0; i < 120; i++) view.Tick(.05f);
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(0f, view.LeftLeg.localEulerAngles.x)), 1f);
        }

        [Test]
        public void SittingBendsTheLegsForwardAndLowersTheBody()
        {
            var view = Make(); view.ApplySnapshot(new PlayerSnapshot(1, 0, 0, 0, 0, 0, 0, 0f, NetworkMotionState.Sit, 0)); for (var i = 0; i < 120; i++) view.Tick(.05f);
            Assert.AreEqual(-85f, Mathf.DeltaAngle(0f, view.LeftLeg.localEulerAngles.x), 1f); Assert.Less(view.Torso.localPosition.y, -.4f);
        }

        [Test]
        public void AGestureLastsItsTimeAndWalkingEndsIt()
        {
            var view = Make(); view.ApplySnapshot(new PlayerSnapshot(1, 0, 0, 0, 0, 0, 0, 0f, NetworkMotionState.Idle, 0));
            view.PlayGesture(0, 1f); Assert.AreEqual(AvatarView.Gesture.Applaud, view.ActiveGesture); view.Tick(.5f); Assert.AreEqual(AvatarView.Gesture.Applaud, view.ActiveGesture);
            view.Tick(.6f); Assert.AreEqual(AvatarView.Gesture.None, view.ActiveGesture);
            view.PlayGesture(2, 5f); view.ApplySnapshot(new PlayerSnapshot(1, 0, 0, 0, 0, 0, -3f, 0f, NetworkMotionState.Walk, 0)); view.Tick(.05f);
            Assert.AreEqual(AvatarView.Gesture.None, view.ActiveGesture);
            view.PlayGesture(99); Assert.AreEqual(AvatarView.Gesture.None, view.ActiveGesture, "um número de gesto que a rede não carrega é ignorado");
        }

        [Test]
        public void HidingTheBodyHidesEveryPartAndShowingBringsThemBack()
        {
            var view = Make(); view.SetVisible(false);
            foreach (var renderer in view.GetComponentsInChildren<Renderer>(true)) Assert.IsFalse(renderer.enabled);
            view.SetVisible(true); Assert.IsTrue(view.Visible);
            foreach (var renderer in view.GetComponentsInChildren<Renderer>(true)) Assert.IsTrue(renderer.enabled);
        }

        [Test]
        public void TheAppearanceIsReadFromTheServerJsonAndBadNumbersAreBroughtIntoRange()
        {
            var a = AvatarAppearance.Parse("{\"displayName\":\"Ana\",\"skinColor\":\"#102030\",\"hairStyle\":\"long\",\"bodySettingsJson\":\"{\\\"height\\\":9,\\\"shoulder\\\":1.05}\",\"equippedItems\":[{\"slot\":\"hat\",\"itemKey\":\"cap_basic\",\"tintColor\":\"#FF0000\"}],\"customModelId\":\"11111111-2222-3333-4444-555555555555\",\"customModelScale\":0.01}");
            Assert.AreEqual("Ana", a.DisplayName); Assert.AreEqual("long", a.HairStyle); Assert.AreEqual(0x10 / 255f, a.Skin.r, .01f);
            Assert.AreEqual(1.15f, a.Height, .001f, "uma altura fora do intervalo volta ao limite"); Assert.AreEqual(1.05f, a.Shoulder, .001f);
            Assert.AreEqual("cap_basic", a.Item("hat", "none")); Assert.AreEqual(1f, a.Tint("hat", Color.black).r, .01f); Assert.IsNotNull(a.CustomModelId);
            var bad = AvatarAppearance.Parse("isto não é json"); Assert.AreEqual("short", bad.HairStyle); Assert.IsNull(bad.CustomModelId);
        }

        [Test]
        public void ADressedBodyHasItsHairHatAndBackpackAndTheHeightSlider()
        {
            var view = Make(); view.ApplyAppearance(AvatarAppearance.Parse("{\"hairStyle\":\"mohawk\",\"beardStyle\":\"full\",\"bodySettingsJson\":\"{\\\"height\\\":1.1}\",\"equippedItems\":[{\"slot\":\"hat\",\"itemKey\":\"cap_basic\"},{\"slot\":\"back\",\"itemKey\":\"backpack_basic\"}]}"));
            Assert.AreEqual(1.1f, view.Torso.localScale.y, .001f); Assert.GreaterOrEqual(view.Dressing, 6);
            view.ApplyAppearance(AvatarAppearance.Parse("{\"hairStyle\":\"bald\"}")); var plain = view.Dressing; Assert.Less(plain, 6, "trocar a aparência tira o que não vale mais");
        }

        [Test]
        public void TheClipsAreAskedForByWhatThePersonIsDoing()
        {
            Assert.AreEqual("Walk", DefaultClips.StateOf(NetworkMotionState.Walk, 3f, false)); Assert.AreEqual("FlyIdle", DefaultClips.StateOf(NetworkMotionState.Fly, 0f, false));
            Assert.AreEqual("FlyMove", DefaultClips.StateOf(NetworkMotionState.Fly, 6f, false)); Assert.AreEqual("SwimIdle", DefaultClips.StateOf(NetworkMotionState.Swim, 0f, false));
            Assert.IsNull(DefaultClips.StateOf(NetworkMotionState.Sit, 0f, false), "sentar não tem clipe"); Assert.IsNull(DefaultClips.StateOf(NetworkMotionState.Idle, 0f, true), "um gesto usa a pose procedural");
        }

        [Test]
        public void TheGameCarriesItsClipFilesAndTheSharedReaderOpensThem()
        {
            foreach (var name in new[] { "default_avatar_animations", "typing_animation", "idle_animation", "jump_animation", "jumpdown_animation" }) Assert.IsNotNull(Resources.Load<TextAsset>("Animations/" + name), name);
            var bytes = Resources.Load<TextAsset>("Animations/default_avatar_animations").bytes;
            var walk = LifeVerse.Shared.Avatar.SkeletonClip.Read(bytes, "Walk"); Assert.IsNotNull(walk); Assert.Greater(walk.Duration, .3f);
            var deltas = walk.SampleDeltas(.2f); Assert.IsTrue(deltas.ContainsKey("upper_leg_l") && deltas.ContainsKey("hips"));
        }

        [Test]
        public void TheHeadNeverBendsPastWhatAHeadCan()
        {
            var view = Make(); view.ApplyHeadLook(3f); Assert.AreEqual(LifeVerse.Shared.Networking.HeadLook.MaxPitch, view.HeadPitch, 1e-5f);
            view.ApplyHeadLook(float.NaN); Assert.AreEqual(0f, view.HeadPitch);
        }

        [Test]
        public void ARemoteBodyFollowsTheSnapshotsAndASnapshotFarAwayMovesItAtOnce()
        {
            var view = Make(); view.ApplySnapshot(new PlayerSnapshot(1, 0, 0, 0, 0, 0, 0, 0f, NetworkMotionState.Idle, 0));
            view.ApplySnapshot(new PlayerSnapshot(1, 1f, 0, 0, 0, 0, 0, 0f, NetworkMotionState.Idle, 0)); for (var i = 0; i < 40; i++) view.Tick(.05f);
            Assert.AreEqual(1f, view.GamePosition.x, .02f);
            view.ApplySnapshot(new PlayerSnapshot(1, 50f, 0, 0, 0, 0, 0, 0f, NetworkMotionState.Idle, 0)); view.Tick(.016f);
            Assert.AreEqual(50f, view.GamePosition.x, .01f);
        }
    }
}
